namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Puerto de envío de una notificación (Story 5.1a). La implementación de Fase 1 emite hacia un sink verificable
/// (consola/MailHog); un SMTP real es un adaptador posterior. Es la costura donde se enchufa el transporte de correo.
/// </summary>
public interface INotificador
{
    Task NotificarAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct);
}
