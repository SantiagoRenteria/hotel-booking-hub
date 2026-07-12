using Hoteles.Domain.Habitaciones;
using Hoteles.Domain.Hoteles;
using Hoteles.Infrastructure.Persistencia;
using Xunit;

namespace Hoteles.IntegrationTests;

/// <summary>
/// Story T.5 — lectura del catálogo aislada por agente, sobre SQL Server real (el aislamiento solo se prueba de
/// verdad contra el motor: el query filter de hoteles + la verificación transitiva del hotel dueño para las
/// habitaciones). Agentes y nombres únicos por test (BD compartida del fixture + índice único de unicidad).
/// </summary>
[Collection("sqlserver-hoteles")]
public sealed class LecturaCatalogoTests(SqlServerFixture fixture)
{
    private readonly string _sfx = Guid.NewGuid().ToString("N");
    private string AgenteA => $"lec-a-{_sfx}@x.com";
    private string AgenteB => $"lec-b-{_sfx}@x.com";

    private async Task<Guid> SembrarHotel(string agente, string nombre, string ciudad = "Bogotá", bool eliminado = false)
    {
        await using var db = fixture.CrearContexto(); // sin identidad → filtro inactivo para sembrar
        var repo = new HotelRepository(db);
        var hotel = Hotel.Crear(nombre, ciudad, "Calle 1", "desc", EstadoHotel.Habilitado, agente);
        var rv = await repo.CrearAsync(hotel, CancellationToken.None);
        if (eliminado)
        {
            hotel.Eliminar();
            await repo.GuardarConcurrenciaAsync(hotel, rv, CancellationToken.None);
        }

        return hotel.Id;
    }

    private async Task<Guid> SembrarHabitacion(Guid hotelId)
    {
        await using var db = fixture.CrearContexto();
        var hab = Habitacion.Crear(hotelId, "Suite", 100m, 19m, "Piso 1", EstadoHabitacion.Habilitada, capacidad: 2);
        await new HabitacionRepository(db).CrearAsync(hab, CancellationToken.None);
        return hab.Id;
    }

    private LectorCatalogoSql LectorDe(string agente) => new(fixture.CrearContextoConAgente(agente));

    [Fact]
    public async Task ListarHoteles_devuelve_solo_los_del_agente_y_sin_eliminados()
    {
        var propio = await SembrarHotel(AgenteA, $"Propio {_sfx}");
        await SembrarHotel(AgenteA, $"Eliminado {_sfx}", eliminado: true);
        await SembrarHotel(AgenteB, $"Ajeno {_sfx}");

        var pagina = await LectorDe(AgenteA).ListarHotelesAsync(1, 100, CancellationToken.None);

        Assert.Contains(pagina.Items, h => h.Id == propio);
        Assert.All(pagina.Items, h => Assert.DoesNotContain("Eliminado", h.Nombre));
        Assert.All(pagina.Items, h => Assert.DoesNotContain("Ajeno", h.Nombre));
    }

    [Fact]
    public async Task Paginacion_respeta_page_pageSize_y_total()
    {
        await SembrarHotel(AgenteA, $"Pag A {_sfx}");
        await SembrarHotel(AgenteA, $"Pag B {_sfx}");
        await SembrarHotel(AgenteA, $"Pag C {_sfx}");

        var lector = LectorDe(AgenteA);
        var p1 = await lector.ListarHotelesAsync(1, 2, CancellationToken.None);
        Assert.Equal(3, p1.Total); // total del agente (aislado), no de la página
        Assert.Equal(2, p1.Items.Count); // page size respetado
        Assert.Equal(1, p1.Page);

        var p2 = await lector.ListarHotelesAsync(2, 2, CancellationToken.None);
        Assert.Single(p2.Items); // 3 hoteles → 2 + 1
        // Sin solapamiento entre páginas (orden estable por Nombre+Id).
        Assert.Empty(p1.Items.Select(h => h.Id).Intersect(p2.Items.Select(h => h.Id)));
    }

    [Fact]
    public async Task ObtenerHotel_dueno_ok_ajeno_y_eliminado_null()
    {
        var propio = await SembrarHotel(AgenteA, $"Propio {_sfx}");
        var ajeno = await SembrarHotel(AgenteB, $"Ajeno {_sfx}");
        var eliminado = await SembrarHotel(AgenteA, $"Elim {_sfx}", eliminado: true);

        var lector = LectorDe(AgenteA);
        var vista = await lector.ObtenerHotelAsync(propio, CancellationToken.None);

        Assert.NotNull(vista);
        Assert.Equal($"Propio {_sfx}", vista!.Nombre);
        Assert.False(string.IsNullOrEmpty(vista.RowVersion)); // sirve para editar tras leer (GET→PUT)
        Assert.Null(await lector.ObtenerHotelAsync(ajeno, CancellationToken.None));
        Assert.Null(await lector.ObtenerHotelAsync(eliminado, CancellationToken.None));
        Assert.Null(await lector.ObtenerHotelAsync(Guid.NewGuid(), CancellationToken.None)); // inexistente
    }

    [Fact]
    public async Task ListarHabitaciones_hotel_propio_lista_hotel_ajeno_null()
    {
        var hotelA = await SembrarHotel(AgenteA, $"ConHab {_sfx}");
        var habId = await SembrarHabitacion(hotelA);
        var hotelB = await SembrarHotel(AgenteB, $"Ajeno {_sfx}");

        var lector = LectorDe(AgenteA);
        var propias = await lector.ListarHabitacionesDeHotelAsync(hotelA, 1, 100, CancellationToken.None);
        Assert.NotNull(propias);
        Assert.Contains(propias!.Items, h => h.Id == habId);

        // El hotel de B es invisible para A → null (404), no una lista vacía.
        Assert.Null(await lector.ListarHabitacionesDeHotelAsync(hotelB, 1, 100, CancellationToken.None));
        Assert.Null(await lector.ListarHabitacionesDeHotelAsync(Guid.NewGuid(), 1, 100, CancellationToken.None));
    }

    [Fact]
    public async Task ObtenerHabitacion_de_hotel_propio_ok_de_hotel_ajeno_null()
    {
        var hotelB = await SembrarHotel(AgenteB, $"Ajeno {_sfx}");
        var habDeB = await SembrarHabitacion(hotelB);

        // A NO puede ver la habitación de un hotel de B (aislamiento transitivo por el hotel dueño).
        Assert.Null(await LectorDe(AgenteA).ObtenerHabitacionAsync(habDeB, CancellationToken.None));
        // El dueño B sí la ve.
        var vista = await LectorDe(AgenteB).ObtenerHabitacionAsync(habDeB, CancellationToken.None);
        Assert.NotNull(vista);
        Assert.Equal(hotelB, vista!.HotelId);
        Assert.Null(await LectorDe(AgenteB).ObtenerHabitacionAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
