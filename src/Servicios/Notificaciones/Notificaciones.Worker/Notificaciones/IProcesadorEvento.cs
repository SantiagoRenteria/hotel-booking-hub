using HotelBookingHub.Comun.Eventos;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Procesa un evento de integración entrante (costura del consumidor). La implementa
/// <see cref="ConsumidorReservaConfirmada"/>; permite envolver el procesamiento con políticas transversales
/// (tope de intentos + dead-letter del <see cref="DespachadorNotificaciones"/>, Story 5.1b Task 4) sin acoplar
/// el despachador a un consumidor concreto.
/// </summary>
public interface IProcesadorEvento
{
    Task ProcesarAsync(EventoIntegracion evento, CancellationToken ct);
}
