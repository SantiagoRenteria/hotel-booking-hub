using HotelBookingHub.Comun.Eventos;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Consumidor de <c>SolicitudCancelacionRegistrada.v1</c> (Story 5.2, AC-E5.2.1): al registrarse una solicitud
/// de cancelación, avisa al viajero con un acuse que incluye la penalidad ETIQUETADA como estimación (no cobro
/// final) y al agente con un aviso de "por resolver". Reutiliza <see cref="INotificador"/> (5.1a) y el inbox
/// idempotente (5.1b) — dedup del efecto por destinatario. STUB (RED): aún no envía.
/// </summary>
public sealed class ConsumidorSolicitudCancelacion(INotificador notificador, IInboxIdempotencia inbox) : IProcesadorEvento
{
    public Task ProcesarAsync(EventoIntegracion evento, CancellationToken ct)
    {
        _ = (notificador, inbox); // TODO 5.2 GREEN: acuse al viajero (estimación) + aviso al agente, idempotente.
        return Task.CompletedTask;
    }
}
