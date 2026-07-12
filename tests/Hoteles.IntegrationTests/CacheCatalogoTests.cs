using Hoteles.Application.Abstracciones;
using Hoteles.Domain.Habitaciones;
using Hoteles.Domain.Hoteles;
using Hoteles.Infrastructure.Cache;
using Hoteles.Infrastructure.Persistencia;
using Xunit;

namespace Hoteles.IntegrationTests;

/// <summary>
/// Story T.6 — caché Redis del catálogo con invalidación por generación. Verifica las dos propiedades que
/// justifican el diseño: (1) HIT real (una segunda lectura no vuelve a SQL) y (2) ANTI-STALE (una escritura por
/// el repo invalida la generación → la lectura siguiente muestra el cambio de inmediato, sin esperar al TTL).
/// Todo end-to-end contra Redis+SQL reales, con el decorador y el invalidador sobre la MISMA conexión.
/// </summary>
[Collection("cache-catalogo")]
public sealed class CacheCatalogoTests(CacheCatalogoFixture fixture)
{
    private readonly string _sfx = Guid.NewGuid().ToString("N");
    private string AgenteA => $"cache-a-{_sfx}@x.com";
    private string AgenteB => $"cache-b-{_sfx}@x.com";

    // Lector cacheado real: inner SQL con identidad de agente + Redis compartido + mismo IContextoAgente.
    private ILectorCatalogo LectorCacheado(string agente) => new LectorCatalogoCacheado(
        new LectorCatalogoSql(fixture.CrearContextoConAgente(agente)),
        fixture.Redis,
        new CacheCatalogoFixture.ContextoAgenteFijo(agente),
        new OpcionesCacheCatalogo());

    // Repo con invalidador conectado al MISMO Redis → sus escrituras bumpean la generación del decorador.
    private HotelRepository RepoConInvalidador() =>
        new(fixture.CrearContexto(), new InvalidadorCacheCatalogoRedis(fixture.Redis));

    // Repo SIN invalidador (no-op): siembra saltándose la invalidación, para exhibir el HIT (lectura obsoleta).
    private HotelRepository RepoSinInvalidador() => new(fixture.CrearContexto());

    private static async Task<Guid> Crear(HotelRepository repo, string agente, string nombre)
    {
        var hotel = Hotel.Crear(nombre, "Bogotá", "Calle 1", "desc", EstadoHotel.Habilitado, agente);
        await repo.CrearAsync(hotel, CancellationToken.None);
        return hotel.Id;
    }

    // Crea un hotel y devuelve (id, rowVersion) para poder editarlo/eliminarlo luego (concurrencia optimista).
    private async Task<(Guid Id, byte[] Rv)> CrearHotelConRv(string agente, string nombre)
    {
        await using var db = fixture.CrearContexto();
        var hotel = Hotel.Crear(nombre, "Bogotá", "Calle 1", "desc", EstadoHotel.Habilitado, agente);
        var rv = await new HotelRepository(db, new InvalidadorCacheCatalogoRedis(fixture.Redis)).CrearAsync(hotel, CancellationToken.None);
        return (hotel.Id, rv);
    }

    private async Task<Guid> SembrarHabitacion(Guid hotelId)
    {
        await using var db = fixture.CrearContexto();
        var hab = Habitacion.Crear(hotelId, "Suite", 100m, 19m, "Piso 1", EstadoHabitacion.Habilitada, capacidad: 2);
        await new HabitacionRepository(db, new InvalidadorCacheCatalogoRedis(fixture.Redis)).CrearAsync(hab, CancellationToken.None);
        return hab.Id;
    }

    [Fact]
    public async Task Segunda_lectura_sirve_de_cache_no_ve_escritura_sin_invalidar()
    {
        var hotel1 = await Crear(RepoConInvalidador(), AgenteA, $"Cache1 {_sfx}");

        var lector = LectorCacheado(AgenteA);
        var p1 = await lector.ListarHotelesAsync(1, 100, CancellationToken.None); // miss → SQL, cachea la página
        Assert.Contains(p1.Items, h => h.Id == hotel1);

        // Escritura QUE NO INVALIDA (repo sin invalidador): si no hubiera caché, la 2ª lectura la vería.
        var hotel2 = await Crear(RepoSinInvalidador(), AgenteA, $"Cache2 {_sfx}");

        var p2 = await lector.ListarHotelesAsync(1, 100, CancellationToken.None); // HIT → página vieja
        Assert.DoesNotContain(p2.Items, h => h.Id == hotel2); // prueba que sirvió de caché (no re-consultó SQL)
    }

    [Fact]
    public async Task Crear_hotel_por_el_repo_invalida_y_la_lectura_lo_muestra_de_inmediato()
    {
        var lector = LectorCacheado(AgenteA);
        await lector.ListarHotelesAsync(1, 100, CancellationToken.None); // calienta la caché (generación 0)

        // Alta por el repo CON invalidador → INCR de la generación del agente.
        var nuevo = await Crear(RepoConInvalidador(), AgenteA, $"AntiStale {_sfx}");

        var pagina = await lector.ListarHotelesAsync(1, 100, CancellationToken.None);
        Assert.Contains(pagina.Items, h => h.Id == nuevo); // criterio duro: se ve YA, no tras el TTL
    }

    [Fact]
    public async Task La_cache_esta_aislada_por_agente()
    {
        // A y B leen; el bump de generación de A no debe afectar la clave de B (claves por agente).
        await Crear(RepoConInvalidador(), AgenteA, $"DeA {_sfx}");
        var deB = await Crear(RepoConInvalidador(), AgenteB, $"DeB {_sfx}");

        var lectorB = LectorCacheado(AgenteB);
        var b1 = await lectorB.ListarHotelesAsync(1, 100, CancellationToken.None);
        Assert.Contains(b1.Items, h => h.Id == deB);
        Assert.DoesNotContain(b1.Items, h => h.Nombre.Contains("DeA")); // aislamiento de datos por agente

        // Una escritura de A invalida solo a A; la caché de B sigue sirviendo su página.
        await Crear(RepoConInvalidador(), AgenteA, $"OtroDeA {_sfx}");
        var b2 = await lectorB.ListarHotelesAsync(1, 100, CancellationToken.None);
        Assert.Contains(b2.Items, h => h.Id == deB);
    }

    // Regresión del hallazgo CRÍTICO del code-review (IDOR): la clave de caché de la LISTA de habitaciones incluía
    // solo el hotelId → un no-dueño leía el hit del dueño. Con el agente en la clave, B nunca ve la entrada de A.
    [Fact]
    public async Task La_lista_de_habitaciones_cacheada_no_se_filtra_a_otro_agente()
    {
        var hotelDeA = await Crear(RepoConInvalidador(), AgenteA, $"ConHab {_sfx}");
        await SembrarHabitacion(hotelDeA);

        // A cachea sus habitaciones del hotel.
        var propias = await LectorCacheado(AgenteA).ListarHabitacionesDeHotelAsync(hotelDeA, 1, 100, CancellationToken.None);
        Assert.NotNull(propias);
        Assert.NotEmpty(propias!.Items);

        // B pide el MISMO hotelId: no es dueño → debe ser null (404), NUNCA el hit cacheado de A.
        var deB = await LectorCacheado(AgenteB).ListarHabitacionesDeHotelAsync(hotelDeA, 1, 100, CancellationToken.None);
        Assert.Null(deB);
    }

    // Regresión del hallazgo MEDIO (anti-stale): eliminar el hotel debe caducar también su caché de habitaciones,
    // si no un GET posterior daría 200 con habitaciones stale dentro del TTL en vez de 404.
    [Fact]
    public async Task Eliminar_el_hotel_invalida_la_cache_de_sus_habitaciones()
    {
        var (hotelId, rv) = await CrearHotelConRv(AgenteA, $"AElim {_sfx}");
        await SembrarHabitacion(hotelId);

        var lector = LectorCacheado(AgenteA);
        var antes = await lector.ListarHabitacionesDeHotelAsync(hotelId, 1, 100, CancellationToken.None); // cachea
        Assert.NotNull(antes);
        Assert.NotEmpty(antes!.Items);

        // Elimina el hotel (baja lógica) por el repo con invalidador.
        await using (var db = fixture.CrearContexto())
        {
            var repo = new HotelRepository(db, new InvalidadorCacheCatalogoRedis(fixture.Redis));
            var hotel = await repo.ObtenerAsync(hotelId, CancellationToken.None);
            hotel!.Eliminar();
            await repo.GuardarConcurrenciaAsync(hotel, rv, CancellationToken.None);
        }

        // El hotel ya no es visible → la lista de habitaciones debe dar 404 (null), no la página cacheada vieja.
        var despues = await lector.ListarHabitacionesDeHotelAsync(hotelId, 1, 100, CancellationToken.None);
        Assert.Null(despues);
    }
}
