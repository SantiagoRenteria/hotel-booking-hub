namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Sink de Fase 1 de <see cref="INotificador"/>: registra el correo en el log (visible en consola/MailHog vía el
/// exportador). Cierra el criterio obligatorio HU2-5 sin depender de un SMTP real (pulido opcional posterior).
/// </summary>
public sealed class NotificadorConsola(ILogger<NotificadorConsola> logger) : INotificador
{
    public Task NotificarAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct)
    {
        logger.LogInformation("Correo a {Destinatario} · Asunto: {Asunto}\n{Cuerpo}", destinatario, asunto, cuerpo);
        return Task.CompletedTask;
    }
}
