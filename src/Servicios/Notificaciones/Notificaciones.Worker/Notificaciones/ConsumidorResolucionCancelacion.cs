using HotelBookingHub.Comun.Eventos;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Consumidor de la resolución de una cancelación (Story 5.3): reacciona a <c>ReservaCancelada.v1</c> (aprobación/
/// condonación, AC-E5.3.1) y a <c>SolicitudCancelacionRechazada.v1</c> (rechazo, AC-E5.3.2), notificando al
/// viajero el desenlace FINAL. Invariante: un rechazo NUNCA comunica cancelación y viceversa. STUB (RED): aún no
/// envía.
/// </summary>
public sealed class ConsumidorResolucionCancelacion(INotificador notificador, IInboxIdempotencia inbox) : IProcesadorEvento
{
    public Task ProcesarAsync(EventoIntegracion evento, CancellationToken ct)
    {
        _ = (notificador, inbox); // TODO 5.3 GREEN.
        return Task.CompletedTask;
    }
}
