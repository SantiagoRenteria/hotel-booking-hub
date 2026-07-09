using HotelBookingHub.Comun.Eventos;
using Notificaciones.Worker.Notificaciones;

namespace Notificaciones.UnitTests;

/// <summary>
/// Story 5.1b — hallazgos del code-review sobre la compensación del envío:
/// F1 (la liberación de la reserva no debe fallar por un <see cref="CancellationToken"/> cancelado ni enmascarar
/// la excepción original) y F2 (un destinatario con fallo permanente no debe impedir el envío al otro).
/// </summary>
public sealed class ConsumidorCompensacionTests
{
    /// <summary>Inbox con semántica SETNX cuya <c>LiberarAsync</c> respeta el <c>ct</c> igual que la impl. Redis
    /// (<c>ThrowIfCancellationRequested</c> al entrar): permite reproducir la fuga de reserva de F1.</summary>
    private sealed class InboxSensibleAlCt : IInboxIdempotencia
    {
        private readonly HashSet<string> _reservas = [];

        public Task<bool> IntentarMarcarProcesadoAsync(Guid messageId, int version, string efecto, CancellationToken ct)
        {
            lock (_reservas)
            {
                return Task.FromResult(_reservas.Add(Clave(messageId, version, efecto)));
            }
        }

        public Task LiberarAsync(Guid messageId, int version, string efecto, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); // imita InboxIdempotenciaRedis
            lock (_reservas)
            {
                _reservas.Remove(Clave(messageId, version, efecto));
            }

            return Task.CompletedTask;
        }

        private static string Clave(Guid messageId, int version, string efecto) =>
            $"notif:inbox:{messageId:N}:{version}:{efecto}";
    }

    private sealed record CorreoEnviado(string Destinatario);

    /// <summary>Notificador que falla SIEMPRE para los destinatarios indicados; el resto se registra.</summary>
    private sealed class NotificadorSelectivo(params string[] destinatariosQueFallan) : INotificador
    {
        private readonly HashSet<string> _fallan = [.. destinatariosQueFallan];

        public List<CorreoEnviado> Enviados { get; } = [];

        public Task NotificarAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct)
        {
            if (_fallan.Contains(destinatario))
            {
                throw new InvalidOperationException($"Fallo permanente enviando a {destinatario}.");
            }

            Enviados.Add(new CorreoEnviado(destinatario));
            return Task.CompletedTask;
        }
    }

    private static ReservaConfirmadaV1 Data() => new(
        AggregateId: Guid.CreateVersion7(),
        HotelId: Guid.NewGuid(),
        HotelNombre: "Hotel Cartagena Real",
        Ciudad: "Cartagena",
        HabitacionId: Guid.NewGuid(),
        Entrada: new DateOnly(2026, 8, 10),
        Salida: new DateOnly(2026, 8, 14),
        HuespedNombre: "Andrés Pérez",
        HuespedEmail: "andres@example.com",
        AgenteEmail: "carolina@agencia.com",
        PrecioTotal: 1428.00m);

    private static EventoIntegracion Envelope(object data) => new(
        Id: Guid.CreateVersion7(),
        Type: ReservaConfirmadaV1.Tipo,
        Version: 1,
        OccurredAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        TraceId: null,
        Data: data);

    [Fact]
    public async Task F1_liberacion_no_falla_por_ct_cancelado_ni_enmascara_la_excepcion_original()
    {
        // Given: el envío al huésped falla y el ct ya está cancelado (apagado graceful a mitad del efecto).
        var inbox = new InboxSensibleAlCt();
        var noti = new NotificadorSelectivo("andres@example.com");
        var consumidor = new ConsumidorReservaConfirmada(noti, inbox);
        var evento = Envelope(Data());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // When: procesar con el ct cancelado.
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => consumidor.ProcesarAsync(evento, cts.Token));

        // Then: la excepción propagada es la ORIGINAL del envío (no una OperationCanceledException de LiberarAsync).
        Assert.IsType<InvalidOperationException>(ex);
        // And: la reserva quedó liberada pese al ct cancelado (re-reservable) → sin pérdida.
        Assert.True(await inbox.IntentarMarcarProcesadoAsync(evento.Id, evento.Version, "huesped", CancellationToken.None));
    }

    [Fact]
    public async Task F2_un_destinatario_con_fallo_permanente_no_impide_el_envio_al_otro()
    {
        // Given: el correo del huésped falla siempre; el del agente es válido.
        var inbox = new InboxSensibleAlCt();
        var noti = new NotificadorSelectivo("andres@example.com");
        var consumidor = new ConsumidorReservaConfirmada(noti, inbox);
        var evento = Envelope(Data());

        // When: procesar (propaga para que el transporte re-entregue el huésped pendiente).
        await Assert.ThrowsAnyAsync<Exception>(() => consumidor.ProcesarAsync(evento, CancellationToken.None));

        // Then: el AGENTE (sano) recibió su correo pese al fallo del huésped; no quedó bloqueado ni perdido.
        Assert.Contains(noti.Enviados, c => c.Destinatario == "carolina@agencia.com");
        // And: el huésped NO se envió y su reserva quedó liberada para el reintento.
        Assert.DoesNotContain(noti.Enviados, c => c.Destinatario == "andres@example.com");
        Assert.True(await inbox.IntentarMarcarProcesadoAsync(evento.Id, evento.Version, "huesped", CancellationToken.None));
    }
}
