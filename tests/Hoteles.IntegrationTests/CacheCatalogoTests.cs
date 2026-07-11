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
}
