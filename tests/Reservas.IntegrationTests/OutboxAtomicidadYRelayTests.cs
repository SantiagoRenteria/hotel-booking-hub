using HotelBookingHub.Comun.Eventos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Reservas.Infrastructure.Outbox;
using Reservas.Infrastructure.Persistencia;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Atomicidad del éxito y resiliencia del relay del productor. En la colección aislada
/// <c>OutboxFaultInjection</c> (contenedor propio, sin paralelización): el relay procesa filas globales del
/// outbox y estos tests limpian al inicio, así que deben quedar fuera del contenedor de 1.5.
/// </summary>
[Collection("OutboxFaultInjection")]
public sealed class OutboxAtomicidadYRelayTests(SqlServerFixture fixture)
{
    private static ProcesadorOutbox Procesador(IPublicadorEventos publicador) =>
        new(publicador, new OpcionesRelayOutbox(), NullLogger<ProcesadorOutbox>.Instance);

    // AC-E1.6b.2 — éxito: una fila de cada una; la de outbox queda Pendiente.
    [Fact]
    public async Task Exito_escribe_una_reserva_y_una_fila_de_outbox_pendiente()
    {
        var reserva = HelpersOutbox.NuevaReserva();
        var messageId = Guid.CreateVersion7();

        await using (var db = fixture.CrearContexto())
        {
            await HelpersOutbox.LimpiarAsync(db, CancellationToken.None);
            await HelpersOutbox.ConfirmarConOutboxAsync(db, reserva, messageId, CancellationToken.None);
        }

        await using var verify = fixture.CrearContexto();
        Assert.Equal(1, await verify.Reservas.CountAsync(r => r.Id == reserva.Id));
        var fila = await verify.OutboxMessages.SingleAsync(o => o.AggregateId == reserva.Id);
        Assert.Equal(OutboxMessage.Pendiente, fila.Estado);
        Assert.Equal(messageId, fila.MessageId);
        Assert.Equal(0, fila.Intentos);
    }

    // AC-E1.6b.4 — el relay publica y marca Enviada con intentos >= 1.
    [Fact]
    [Trait("VerificationDebt", "E5:ConsumerIdempotency")]
    [Trait("VerificationDebt", "E3:ProjectionOrder")]
    public async Task Relay_publica_pendiente_y_la_marca_enviada()
    {
        var reserva = HelpersOutbox.NuevaReserva();
        await using (var db = fixture.CrearContexto())
        {
            await HelpersOutbox.LimpiarAsync(db, CancellationToken.None);
            await HelpersOutbox.ConfirmarConOutboxAsync(db, reserva, Guid.CreateVersion7(), CancellationToken.None);
        }

        var publicador = new PublicadorEventosFake();
        await using (var db = fixture.CrearContexto())
        {
            var enviadas = await Procesador(publicador).ProcesarLoteAsync(db, DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.True(enviadas >= 1);
        }

        Assert.Contains(publicador.Publicados, e => e.Type == ReservaConfirmadaV1.Tipo);
        await using var verify = fixture.CrearContexto();
        var fila = await verify.OutboxMessages.SingleAsync(o => o.AggregateId == reserva.Id);
        Assert.Equal(OutboxMessage.Enviada, fila.Estado);
        Assert.True(fila.Intentos >= 1);
    }

    // AC-E1.6b.4 — sin mark-sent prematuro: si el broker está caído, la fila sigue Pendiente y se reintenta.
    [Fact]
    public async Task Fallo_al_publicar_no_marca_enviada_y_reintenta_al_vencer_el_lease()
    {
        var reserva = HelpersOutbox.NuevaReserva();
        await using (var db = fixture.CrearContexto())
        {
            await HelpersOutbox.LimpiarAsync(db, CancellationToken.None);
            await HelpersOutbox.ConfirmarConOutboxAsync(db, reserva, Guid.CreateVersion7(), CancellationToken.None);
        }

        var t0 = DateTimeOffset.UtcNow;

        // 1er ciclo con broker caído: reclama (intentos incrementa) pero NO marca Enviada.
        await using (var db = fixture.CrearContexto())
        {
            await Procesador(new PublicadorEventosQueFalla()).ProcesarLoteAsync(db, t0, CancellationToken.None);
        }

        await using (var check = fixture.CrearContexto())
        {
            var fila = await check.OutboxMessages.SingleAsync(o => o.AggregateId == reserva.Id);
            Assert.Equal(OutboxMessage.Pendiente, fila.Estado);
            Assert.True(fila.Intentos >= 1);
        }

        // 2º ciclo tras vencer el lease con broker sano: ahora sí se envía (at-least-once, deliveries >= 1).
        var publicador = new PublicadorEventosFake();
        await using (var db = fixture.CrearContexto())
        {
            await Procesador(publicador).ProcesarLoteAsync(db, t0.AddSeconds(60), CancellationToken.None);
        }

        Assert.Single(publicador.Publicados);
        await using var verify = fixture.CrearContexto();
        var final = await verify.OutboxMessages.SingleAsync(o => o.AggregateId == reserva.Id);
        Assert.Equal(OutboxMessage.Enviada, final.Estado);
        Assert.True(final.Intentos >= 2);
    }
}
