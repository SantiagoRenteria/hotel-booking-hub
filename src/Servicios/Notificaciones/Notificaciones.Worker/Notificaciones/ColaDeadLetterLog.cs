using HotelBookingHub.Comun.Eventos;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Sink de dead-letter de Fase 1 (Story 5.1b): registra el mensaje-veneno apartado en el log. Es la costura donde
/// un despliegue real enchufaría una cola dedicada (p. ej. un topic <c>*.deadletter</c>) para inspección/replay.
/// Placeholder verificable, mismo criterio que <see cref="NotificadorConsola"/>.
/// </summary>
public sealed class ColaDeadLetterLog(ILogger<ColaDeadLetterLog> logger) : IColaDeadLetter
{
    public Task EnviarAsync(EventoIntegracion evento, string motivo, int intentos, CancellationToken ct)
    {
        logger.LogError(
            "Mensaje-veneno apartado a dead-letter: Id={MessageId} Type={Tipo} Intentos={Intentos} Motivo={Motivo}",
            evento.Id, evento.Type, intentos, motivo);
        return Task.CompletedTask;
    }
}
