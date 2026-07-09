using HotelBookingHub.Comun.Eventos;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Destino de los mensajes-veneno (Story 5.1b Task 4): un evento que falla SIEMPRE al procesar se aparta aquí
/// tras agotar el tope de intentos, en vez de re-reclamarse sin cota (lo que bloquearía el stream). Paridad con
/// la deuda de dead-letter registrada para el relay de E1/E2.
/// </summary>
public interface IColaDeadLetter
{
    /// <summary>Aparta el evento venenoso, conservando el motivo del último fallo y el nº de intentos agotados.</summary>
    Task EnviarAsync(EventoIntegracion evento, string motivo, int intentos, CancellationToken ct);
}
