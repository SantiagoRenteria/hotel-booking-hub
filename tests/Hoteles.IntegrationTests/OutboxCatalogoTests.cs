using System.Text.Json.Nodes;
using HotelBookingHub.Comun.Eventos;
using Hoteles.Application.Habitaciones.CambiarEstadoHabitacion;
using Hoteles.Application.Habitaciones.CrearHabitacion;
using Hoteles.Application.Habitaciones.EditarHabitacion;
using Hoteles.Domain.Habitaciones;
using Hoteles.Domain.Hoteles;
using Hoteles.Infrastructure.Mensajeria;
using Hoteles.Infrastructure.Outbox;
using Hoteles.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hoteles.IntegrationTests;

/// <summary>
/// Story 2.5 — write-path transaccional de catálogo contra SQL Server real (Opción U): el cambio de dominio y
/// la fila de outbox se persisten en un ÚNICO <c>SaveChanges</c> (misma transacción implícita), y el relay
/// entrega at-least-once. La atomicidad y el order key monotónico SOLO se prueban de verdad contra el motor;
/// un doble en memoria no respeta transacciones. Colección serial: se limpia el outbox al inicio de cada test.
/// </summary>
[Collection("sqlserver-hoteles")]
public sealed class OutboxCatalogoTests(SqlServerFixture fixture)
{
    private static ProcesadorOutbox Procesador(IPublicadorEventos publicador) =>
        new(publicador, new OpcionesRelayOutbox(), NullLogger<ProcesadorOutbox>.Instance);

    private static Task LimpiarOutboxAsync(HotelesDbContext db) =>
        db.Database.ExecuteSqlRawAsync("DELETE FROM OutboxMessages;");

    private async Task<Guid> CrearHotelAsync()
    {
        await using var db = fixture.CrearContexto();
        var hotel = Hotel.Crear("Hotel Central", "Medellín", "Calle 1", "Boutique", EstadoHotel.Habilitado);
        await new HotelRepository(db).CrearAsync(hotel, CancellationToken.None);
        return hotel.Id;
    }

    private static async Task<(Guid Id, byte[] RowVersion)> CrearHabitacionAsync(HotelesDbContext db, Guid hotelId)
    {
        var handler = new CrearHabitacionCommandHandler(
            new HotelRepository(db), new HabitacionRepository(db), new ColaOutbox(db));
        var comando = new CrearHabitacionCommand(hotelId, "Suite", 100m, 19m, "Piso 3", EstadoHabitacion.Habilitada);
        var r = await handler.Handle(comando, CancellationToken.None);
        return (r.Valor!.Id, Convert.FromBase64String(r.Valor.RowVersion));
    }

    private static async Task<byte[]> EditarPrecioAsync(HotelesDbContext db, Guid id, byte[] rowVersion, decimal costo)
    {
        var handler = new EditarHabitacionCommandHandler(new HabitacionRepository(db), new ColaOutbox(db));
        var r = await handler.Handle(
            new EditarHabitacionCommand(id, rowVersion, "Suite", costo, 19m, "Piso 3"), CancellationToken.None);
        return Convert.FromBase64String(r.Valor!.RowVersion);
    }

    // T1 (AC-E2.5.2) — el cambio de dominio y el evento se escriben juntos: 1 habitación + 1 fila outbox Pendiente.
    [Fact]
    public async Task Crear_habitacion_persiste_dominio_y_una_fila_de_outbox_pendiente()
    {
        var hotelId = await CrearHotelAsync();

        Guid habId;
        await using (var db = fixture.CrearContexto())
        {
            (habId, _) = await CrearHabitacionAsync(db, hotelId);
        }

        await using var verify = fixture.CrearContexto();
        Assert.Equal(1, await verify.Habitaciones.CountAsync(h => h.Id == habId));
        var fila = await verify.OutboxMessages.SingleAsync(o => o.AggregateId == habId);
        Assert.Equal(HabitacionAgregadaV1.Tipo, fila.Type);
        Assert.Equal(OutboxMessage.Pendiente, fila.Estado);
        Assert.Equal(0, fila.Intentos);
    }

    // T2 (AC-E2.5.2, el crítico) — fallo inyectado al escribir el outbox: NI la habitación NI la fila quedan.
    [Fact]
    public async Task Fallo_al_escribir_el_outbox_no_deja_ni_la_habitacion_ni_el_evento()
    {
        var hotelId = await CrearHotelAsync();

        await using (var limpio = fixture.CrearContexto())
        {
            await LimpiarOutboxAsync(limpio);
        }

        await using (var db = fixture.CrearContexto(new InterceptorFallaOutbox()))
        {
            var handler = new CrearHabitacionCommandHandler(
                new HotelRepository(db), new HabitacionRepository(db), new ColaOutbox(db));
            var comando = new CrearHabitacionCommand(hotelId, "Suite", 100m, 19m, "Piso 3", EstadoHabitacion.Habilitada);
            await Assert.ThrowsAnyAsync<Exception>(() => handler.Handle(comando, CancellationToken.None));
        }

        // El handler falló al escribir el outbox: NADA nuevo debe quedar bajo ese hotel ni en el outbox.
        await using var verify = fixture.CrearContexto();
        Assert.Equal(0, await verify.Habitaciones.CountAsync(h => h.HotelId == hotelId));
        Assert.Equal(0, await verify.OutboxMessages.CountAsync());
    }

    // T5 (AC-E2.5.4) — order key monotónico: alta + 2 cambios de precio + deshabilitar → versions 1,2,3,4 contiguas.
    [Fact]
    public async Task Las_mutaciones_emisoras_producen_versions_monotonicas_y_contiguas()
    {
        var hotelId = await CrearHotelAsync();
        Guid habId;

        await using (var db = fixture.CrearContexto())
        {
            await LimpiarOutboxAsync(db);
        }

        await using (var db = fixture.CrearContexto())
        {
            byte[] rv;
            (habId, rv) = await CrearHabitacionAsync(db, hotelId); // v1
            rv = await EditarPrecioAsync(db, habId, rv, 150m); // v2
            rv = await EditarPrecioAsync(db, habId, rv, 175m); // v3

            var estado = new CambiarEstadoHabitacionCommandHandler(new HabitacionRepository(db), new ColaOutbox(db));
            await estado.Handle(
                new CambiarEstadoHabitacionCommand(habId, rv, EstadoHabitacion.Deshabilitada), CancellationToken.None); // v4
        }

        await using var verify = fixture.CrearContexto();
        var filas = await verify.OutboxMessages
            .Where(o => o.AggregateId == habId)
            .OrderBy(o => o.Seq)
            .ToListAsync();

        var versions = filas.Select(f => VersionDelPayload(f.Payload)).ToList();
        Assert.Equal([1, 2, 3, 4], versions);
    }

    // T6 (AC-E2.5.2) — el relay publica la pendiente y la marca Enviada con intentos >= 1.
    [Fact]
    public async Task El_relay_publica_la_pendiente_y_la_marca_enviada()
    {
        var hotelId = await CrearHotelAsync();
        Guid habId;

        await using (var db = fixture.CrearContexto())
        {
            await LimpiarOutboxAsync(db);
            (habId, _) = await CrearHabitacionAsync(db, hotelId);
        }

        var publicador = new PublicadorEventosFake();
        await using (var db = fixture.CrearContexto())
        {
            var enviadas = await Procesador(publicador).ProcesarLoteAsync(db, DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.True(enviadas >= 1);
        }

        Assert.Contains(publicador.Publicados, e => e.Type == HabitacionAgregadaV1.Tipo);
        await using var verify = fixture.CrearContexto();
        var fila = await verify.OutboxMessages.SingleAsync(o => o.AggregateId == habId);
        Assert.Equal(OutboxMessage.Enviada, fila.Estado);
        Assert.True(fila.Intentos >= 1);
    }

    // T7 (AC-E2.5.2) — at-least-once: si el broker falla, la fila sigue Pendiente y se reintenta al vencer el lease.
    [Fact]
    public async Task Fallo_al_publicar_no_marca_enviada_y_reintenta_al_vencer_el_lease()
    {
        var hotelId = await CrearHotelAsync();
        Guid habId;

        await using (var db = fixture.CrearContexto())
        {
            await LimpiarOutboxAsync(db);
            (habId, _) = await CrearHabitacionAsync(db, hotelId);
        }

        var t0 = DateTimeOffset.UtcNow;

        await using (var db = fixture.CrearContexto())
        {
            await Procesador(new PublicadorEventosQueFalla()).ProcesarLoteAsync(db, t0, CancellationToken.None);
        }

        await using (var check = fixture.CrearContexto())
        {
            var fila = await check.OutboxMessages.SingleAsync(o => o.AggregateId == habId);
            Assert.Equal(OutboxMessage.Pendiente, fila.Estado);
            Assert.True(fila.Intentos >= 1);
        }

        var publicador = new PublicadorEventosFake();
        await using (var db = fixture.CrearContexto())
        {
            await Procesador(publicador).ProcesarLoteAsync(db, t0.AddSeconds(60), CancellationToken.None);
        }

        Assert.Single(publicador.Publicados);
        await using var verify = fixture.CrearContexto();
        var final = await verify.OutboxMessages.SingleAsync(o => o.AggregateId == habId);
        Assert.Equal(OutboxMessage.Enviada, final.Estado);
        Assert.True(final.Intentos >= 2);
    }

    private static int VersionDelPayload(string payload)
    {
        var envelope = JsonNode.Parse(payload)!.AsObject();
        return envelope["version"]!.GetValue<int>();
    }
}
