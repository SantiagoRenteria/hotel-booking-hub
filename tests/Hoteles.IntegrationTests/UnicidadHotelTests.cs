using HotelBookingHub.Comun.Excepciones;
using Hoteles.Domain.Hoteles;
using Hoteles.Infrastructure.Persistencia;
using Xunit;

namespace Hoteles.IntegrationTests;

/// <summary>
/// Regla de negocio (decisión de Santiago 2026-07-11): un agente no puede tener dos hoteles <b>no eliminados</b>
/// con el mismo <c>(Nombre, Ciudad)</c>. Arbitrado por un índice único filtrado sobre SQL Server real (solo el
/// motor lo garantiza bajo concurrencia; mismo espíritu que el anti-overbooking, ADR-016). Acotado por agente:
/// dos agentes pueden repetir nombre+ciudad en sus catálogos. La baja lógica libera el par. Cubre alta y edición.
/// </summary>
[Collection("sqlserver-hoteles")]
public sealed class UnicidadHotelTests(SqlServerFixture fixture)
{
    private static Hotel NuevoHotel(string agente, string nombre, string ciudad) =>
        Hotel.Crear(nombre, ciudad, "Calle 1", "desc", EstadoHotel.Habilitado, agente);

    [Fact]
    public async Task Mismo_agente_con_mismo_nombre_y_ciudad_lanza_HotelDuplicadoException()
    {
        const string agente = "dup-1@agencia.com";
        await using var db = fixture.CrearContexto();
        var repo = new HotelRepository(db);
        await repo.CrearAsync(NuevoHotel(agente, "Hotel Único 1", "Bogotá"), CancellationToken.None);

        await Assert.ThrowsAsync<HotelDuplicadoException>(() =>
            repo.CrearAsync(NuevoHotel(agente, "Hotel Único 1", "Bogotá"), CancellationToken.None));
    }

    [Fact]
    public async Task Mismo_nombre_y_ciudad_de_agentes_distintos_se_permite()
    {
        await using var db = fixture.CrearContexto();
        var repo = new HotelRepository(db);
        await repo.CrearAsync(NuevoHotel("dup-2a@agencia.com", "Hotel Único 2", "Cali"), CancellationToken.None);

        // Otro agente: mismo nombre+ciudad, catálogo independiente → sin conflicto (clave del índice incluye el agente).
        var ex = await Record.ExceptionAsync(() =>
            repo.CrearAsync(NuevoHotel("dup-2b@agencia.com", "Hotel Único 2", "Cali"), CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Mismo_nombre_en_distinta_ciudad_del_mismo_agente_se_permite()
    {
        const string agente = "dup-3@agencia.com";
        await using var db = fixture.CrearContexto();
        var repo = new HotelRepository(db);
        await repo.CrearAsync(NuevoHotel(agente, "Hotel Único 3", "Medellín"), CancellationToken.None);

        var ex = await Record.ExceptionAsync(() =>
            repo.CrearAsync(NuevoHotel(agente, "Hotel Único 3", "Barranquilla"), CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Un_hotel_eliminado_libera_el_par_nombre_ciudad()
    {
        const string agente = "dup-4@agencia.com";
        await using var db = fixture.CrearContexto();
        var repo = new HotelRepository(db);
        var hotel = NuevoHotel(agente, "Hotel Único 4", "Cartagena");
        var rowVersion = await repo.CrearAsync(hotel, CancellationToken.None);

        hotel.Eliminar(); // baja lógica → el índice filtrado (WHERE Eliminado = 0) deja de contarlo
        await repo.GuardarConcurrenciaAsync(hotel, rowVersion, CancellationToken.None);

        // Recrear el mismo par ahora es válido.
        var ex = await Record.ExceptionAsync(() =>
            repo.CrearAsync(NuevoHotel(agente, "Hotel Único 4", "Cartagena"), CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Editar_un_hotel_a_un_par_ya_usado_lanza_HotelDuplicadoException()
    {
        const string agente = "dup-5@agencia.com";
        await using var db = fixture.CrearContexto();
        var repo = new HotelRepository(db);
        await repo.CrearAsync(NuevoHotel(agente, "Hotel Único 5A", "Pereira"), CancellationToken.None);
        var hotelB = NuevoHotel(agente, "Hotel Único 5B", "Pereira");
        var rvB = await repo.CrearAsync(hotelB, CancellationToken.None);

        // Renombrar B al par (Nombre, Ciudad) de A → colisión en el UPDATE.
        hotelB.Editar("Hotel Único 5A", "Pereira", "Calle 1", "desc");
        await Assert.ThrowsAsync<HotelDuplicadoException>(() =>
            repo.GuardarConcurrenciaAsync(hotelB, rvB, CancellationToken.None));
    }
}
