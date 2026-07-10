using Microsoft.EntityFrameworkCore;
using Reservas.Domain.Reservas;
using Reservas.Infrastructure.Persistencia;
using Reservas.Infrastructure.Proyeccion;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Story 3.3 — persistencia de huéspedes/contacto/agente/precio en la reserva (round-trip real) y lectura del
/// listado/detalle del agente contra SQL Server, con aislamiento server-side (AC-E3.3.2). Agentes/habitaciones
/// únicos por test para aislar sin reset.
/// </summary>
[Collection("sqlserver")]
public sealed class ListadoReservasAgenteTests(SqlServerFixture fixture)
{
    private static readonly DateOnly _entrada = new(2027, 1, 10);
    private static readonly DateOnly _salida = new(2027, 1, 12);

    private static string AgenteUnico() => $"agente-{Guid.NewGuid():N}@test.com";

    private static Reserva NuevaReserva(Guid habId, string agente, decimal precio = 500m)
    {
        var huesped = Huesped.Crear("Ana", "García", new DateOnly(1990, 5, 1), "F",
            Documento.Crear("CC", "123456789"), "ana@test.com", "+573001112233");
        var contacto = ContactoEmergencia.Crear("Luis García", "+573004445566");
        return Reserva.Crear(habId, Estancia.Crear(_entrada, _salida), [huesped], contacto, agente, precio);
    }

    private async Task<Guid> GuardarAsync(Reserva reserva)
    {
        await using var db = fixture.CrearContexto();
        db.Reservas.Add(reserva);
        await db.SaveChangesAsync();
        return reserva.Id;
    }

    private async Task SembrarProyeccionAsync(Guid habId, Guid hotelId, string ciudad)
    {
        await using var db = fixture.CrearContexto();
        var fila = ProyeccionHabitacion.Nueva(habId);
        fila.AplicarAgregada(hotelId, ciudad, "Suite", "Piso 3", 4, 100m, 19m, "Habilitada", 1);
        db.ProyeccionesHabitacion.Add(fila);
        await db.SaveChangesAsync();
    }

    private LectorReservasAgenteSql Lector(ReservasDbContext db) => new(db);

    // Task 1 — round-trip: huéspedes (owned collection) + contacto (owned) + agente + precio persisten.
    [Fact]
    public async Task Persiste_huespedes_contacto_agente_y_precio()
    {
        var agente = AgenteUnico();
        var id = await GuardarAsync(NuevaReserva(Guid.CreateVersion7(), agente, precio: 750m));

        await using var db = fixture.CrearContexto();
        var reserva = await db.Reservas.SingleAsync(r => r.Id == id);

        Assert.Equal(agente, reserva.AgenteEmail);
        Assert.Equal(750m, reserva.PrecioTotal);
        var huesped = Assert.Single(reserva.Huespedes);
        Assert.Equal("Ana", huesped.Nombres);
        Assert.Equal("CC", huesped.Documento.Tipo);
        Assert.NotNull(reserva.ContactoEmergencia);
        Assert.Equal("Luis García", reserva.ContactoEmergencia!.NombreCompleto);
    }

    // AC-E3.3.1 — el listado muestra hotel, habitación, estancia, estado y precio.
    [Fact]
    public async Task Listado_muestra_los_datos_de_la_reserva()
    {
        var agente = AgenteUnico();
        var habId = Guid.CreateVersion7();
        var hotelId = Guid.CreateVersion7();
        var ciudad = "Bogotá-" + Guid.NewGuid().ToString("N")[..6];
        await SembrarProyeccionAsync(habId, hotelId, ciudad);
        var id = await GuardarAsync(NuevaReserva(habId, agente, precio: 500m));

        await using var db = fixture.CrearContexto();
        var lista = await Lector(db).ListarAsync(agente, CancellationToken.None);

        var item = Assert.Single(lista);
        Assert.Equal(id, item.Id);
        Assert.Equal(hotelId, item.HotelId);
        Assert.Equal(ciudad, item.Ciudad);
        Assert.Equal("Suite", item.Tipo);
        Assert.Equal(_entrada, item.Entrada);
        Assert.Equal("Confirmada", item.Estado);
        Assert.Equal(500m, item.PrecioTotal);
    }

    // AC-E3.3.2 — las reservas de OTRO agente no aparecen en mi listado (aislamiento server-side).
    [Fact]
    public async Task Listado_no_incluye_reservas_de_otro_agente()
    {
        var agenteA = AgenteUnico();
        var agenteB = AgenteUnico();
        await GuardarAsync(NuevaReserva(Guid.CreateVersion7(), agenteA));
        await GuardarAsync(NuevaReserva(Guid.CreateVersion7(), agenteB));

        await using var db = fixture.CrearContexto();
        var listaA = await Lector(db).ListarAsync(agenteA, CancellationToken.None);

        Assert.All(listaA, r => Assert.NotEqual(default, r.Id));
        Assert.Single(listaA); // solo la de A
    }

    // Borde — un agente sin reservas obtiene lista vacía.
    [Fact]
    public async Task Agente_sin_reservas_obtiene_lista_vacia()
    {
        await using var db = fixture.CrearContexto();
        var lista = await Lector(db).ListarAsync(AgenteUnico(), CancellationToken.None);
        Assert.Empty(lista);
    }

    // AC-E3.3.1 — el detalle añade huéspedes + contacto.
    [Fact]
    public async Task Detalle_incluye_huespedes_y_contacto()
    {
        var agente = AgenteUnico();
        var id = await GuardarAsync(NuevaReserva(Guid.CreateVersion7(), agente));

        await using var db = fixture.CrearContexto();
        var detalle = await Lector(db).ObtenerDetalleAsync(id, agente, CancellationToken.None);

        Assert.NotNull(detalle);
        Assert.Equal(id, detalle!.Reserva.Id);
        var huesped = Assert.Single(detalle.Huespedes);
        Assert.Equal("ana@test.com", huesped.Email);
        Assert.Equal("CC", huesped.TipoDocumento);
        Assert.NotNull(detalle.ContactoEmergencia);
        Assert.Equal("+573004445566", detalle.ContactoEmergencia!.Telefono);
    }

    // AC-E3.3.2 — el detalle de una reserva ajena devuelve null (→ 404 en el handler), sin filtrar contenido.
    [Fact]
    public async Task Detalle_de_reserva_ajena_devuelve_null()
    {
        var agenteA = AgenteUnico();
        var agenteB = AgenteUnico();
        var idDeA = await GuardarAsync(NuevaReserva(Guid.CreateVersion7(), agenteA));

        await using var db = fixture.CrearContexto();
        var detalle = await Lector(db).ObtenerDetalleAsync(idDeA, agenteB, CancellationToken.None);

        Assert.Null(detalle);
    }
}
