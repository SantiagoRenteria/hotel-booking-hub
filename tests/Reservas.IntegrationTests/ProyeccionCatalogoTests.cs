using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Microsoft.EntityFrameworkCore;
using Reservas.Infrastructure.Persistencia;
using Reservas.Infrastructure.Proyeccion;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Story 3.1 — el consumidor real (<see cref="ProyectorCatalogo"/>) contra SQL Server: convergencia bajo
/// desorden, idempotencia por <c>MessageId</c> (inbox en la misma transacción que el upsert) y orden completo.
/// El envelope se serializa y re-deserializa para que <c>data</c> llegue como <c>JsonElement</c>, igual que en
/// el transporte real. Determinista: los eventos se procesan en el orden que fija cada test.
/// </summary>
[Collection("sqlserver")]
public sealed class ProyeccionCatalogoTests(SqlServerFixture fixture)
{
    private static readonly JsonSerializerOptions _web = new(JsonSerializerDefaults.Web);

    private static EventoIntegracion Envelope(Guid messageId, string tipo, int version, object data)
    {
        var e = new EventoIntegracion(messageId, tipo, version, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), null, data);
        // Round-trip: tras el transporte, Data llega como JsonElement (no como el record concreto).
        return JsonSerializer.Deserialize<EventoIntegracion>(JsonSerializer.Serialize(e, _web), _web)!;
    }

    private static Task LimpiarAsync(ReservasDbContext db) =>
        db.Database.ExecuteSqlRawAsync("DELETE FROM ProyeccionHabitacion; DELETE FROM MensajesProcesados;");

    private async Task ProcesarAsync(EventoIntegracion evento)
    {
        await using var db = fixture.CrearContexto(); // scope por mensaje, como en producción
        await new ProyectorCatalogo(db).ProcesarAsync(evento, CancellationToken.None);
    }

    // AC-E3.1.1 E2E — el precio (v2) llega ANTES del alta (v1): converge a precio v2 + catálogo del alta.
    [Fact]
    public async Task Precio_antes_del_alta_converge_en_SQL()
    {
        var hab = Guid.CreateVersion7();
        var hotel = Guid.CreateVersion7();
        await using (var db = fixture.CrearContexto())
        {
            await LimpiarAsync(db);
        }

        await ProcesarAsync(Envelope(Guid.CreateVersion7(), PrecioHabitacionCambiadoV1.Tipo, 2,
            new PrecioHabitacionCambiadoV1(hab, hotel, 200m, 30m)));
        await ProcesarAsync(Envelope(Guid.CreateVersion7(), HabitacionAgregadaV1.Tipo, 1,
            new HabitacionAgregadaV1(hab, hotel, "Suite", 100m, 19m, "Piso 3", "Habilitada", "Bogotá", 4)));

        await using var verify = fixture.CrearContexto();
        var fila = await verify.ProyeccionesHabitacion.SingleAsync(p => p.HabitacionId == hab);
        Assert.True(fila.Hidratada);
        Assert.Equal("Bogotá", fila.Ciudad);
        Assert.Equal(4, fila.Capacidad);
        Assert.Equal(200m, fila.CostoBase); // precio v2 gana; el alta v1 no lo pisa
    }

    // AC-E3.1.2/4 E2E — el mismo evento (mismo MessageId) entregado dos veces deja una sola fila y no duplica
    // el inbox: la segunda entrega es no-op (PK del inbox en la misma tx que el upsert).
    [Fact]
    public async Task Mismo_MessageId_dos_veces_es_idempotente()
    {
        var hab = Guid.CreateVersion7();
        var hotel = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        await using (var db = fixture.CrearContexto())
        {
            await LimpiarAsync(db);
        }

        var evento = Envelope(messageId, HabitacionAgregadaV1.Tipo, 1,
            new HabitacionAgregadaV1(hab, hotel, "Suite", 100m, 19m, "Piso 3", "Habilitada", "Cali", 2));
        for (var i = 0; i < 4; i++)
        {
            await ProcesarAsync(evento); // misma entrega ×4 (at-least-once agresivo)
        }

        await using var verify = fixture.CrearContexto();
        Assert.Equal(1, await verify.ProyeccionesHabitacion.CountAsync(p => p.HabitacionId == hab));
        Assert.Equal(1, await verify.MensajesProcesados.CountAsync(m => m.MessageId == messageId));
    }

    // Orden completo — alta → precio → deshabilitar: estado final coherente en SQL.
    [Fact]
    public async Task Orden_completo_converge_en_SQL()
    {
        var hab = Guid.CreateVersion7();
        var hotel = Guid.CreateVersion7();
        await using (var db = fixture.CrearContexto())
        {
            await LimpiarAsync(db);
        }

        await ProcesarAsync(Envelope(Guid.CreateVersion7(), HabitacionAgregadaV1.Tipo, 1,
            new HabitacionAgregadaV1(hab, hotel, "Suite", 100m, 19m, "Piso 3", "Habilitada", "Bogotá", 2)));
        await ProcesarAsync(Envelope(Guid.CreateVersion7(), PrecioHabitacionCambiadoV1.Tipo, 2,
            new PrecioHabitacionCambiadoV1(hab, hotel, 150m, 28.5m)));
        await ProcesarAsync(Envelope(Guid.CreateVersion7(), HabitacionDeshabilitadaV1.Tipo, 3,
            new HabitacionDeshabilitadaV1(hab, hotel)));

        await using var verify = fixture.CrearContexto();
        var fila = await verify.ProyeccionesHabitacion.SingleAsync(p => p.HabitacionId == hab);
        Assert.Equal(150m, fila.CostoBase);
        Assert.Equal("Deshabilitada", fila.Estado);
        Assert.Equal(3, fila.VersionEstado);
    }
}
