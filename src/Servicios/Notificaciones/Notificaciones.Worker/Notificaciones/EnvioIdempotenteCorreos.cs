using HotelBookingHub.Comun.Eventos;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>Un correo a enviar como efecto idempotente de un evento.</summary>
/// <param name="Efecto">Discriminador del efecto dentro del mensaje (p. ej. destinatario: "huesped"/"agente"/
///   "viajero"); es lo que deduplica el inbox junto con <c>(MessageId, version)</c>.</param>
/// <param name="Destinatario">Dirección de correo; si es nula/vacía el correo se OMITE (no hay a quién notificar).</param>
public sealed record Correo(string Efecto, string? Destinatario, string Asunto, string Cuerpo);

/// <summary>
/// Envía un lote de correos como efectos idempotentes de un evento (Story 5.2, extraído de 5.1b): cada correo se
/// deduplica por <c>(MessageId, version, efecto)</c> con el patrón <b>reservar(SETNX)→enviar→liberar-si-falla</b>.
/// Intenta TODOS los correos aunque uno falle (un destinatario enfermo no bloquea al sano), agrega los fallos y
/// los propaga al final para que el transporte re-entregue (at-least-once) sin duplicar los ya enviados. Un
/// destinatario nulo se omite. La liberación de compensación usa <see cref="CancellationToken.None"/> y es
/// best-effort (no enmascara la excepción original) — hallazgos F1/F2 de 5.1b.
/// </summary>
public sealed class EnvioIdempotenteCorreos(INotificador notificador, IInboxIdempotencia inbox)
{
    public async Task EnviarLoteAsync(EventoIntegracion evento, IReadOnlyList<Correo> correos, CancellationToken ct)
    {
        var fallos = new List<Exception>();
        foreach (var correo in correos)
        {
            if (string.IsNullOrWhiteSpace(correo.Destinatario))
            {
                continue; // sin dirección → no hay a quién notificar; se omite el efecto.
            }

            var fallo = await IntentarEnviarAsync(evento, correo, ct);
            if (fallo is not null)
            {
                fallos.Add(fallo);
            }
        }

        if (fallos.Count == 1)
        {
            throw fallos[0]; // preserva el tipo original (útil para clasificación de veneno aguas arriba).
        }

        if (fallos.Count > 1)
        {
            throw new AggregateException("Falló el envío de la notificación a uno o más destinatarios.", fallos);
        }
    }

    private async Task<Exception?> IntentarEnviarAsync(EventoIntegracion evento, Correo correo, CancellationToken ct)
    {
        if (!await inbox.IntentarMarcarProcesadoAsync(evento.Id, evento.Version, correo.Efecto, ct))
        {
            return null; // Efecto ya realizado (o en curso) → dedup: no se duplica.
        }

        try
        {
            await notificador.NotificarAsync(correo.Destinatario!, correo.Asunto, correo.Cuerpo, ct);
            return null;
        }
        catch (Exception ex)
        {
            // Liberación de compensación con CancellationToken.None (F1): un ct cancelado (apagado) no debe impedir
            // liberar, o el correo se perdería hasta el TTL. Best-effort: no enmascarar la excepción original.
            try
            {
                await inbox.LiberarAsync(evento.Id, evento.Version, correo.Efecto, CancellationToken.None);
            }
            catch (Exception exLiberar) when (exLiberar is not OperationCanceledException)
            {
                // Liberación best-effort; se ignora para no ocultar la causa real (la excepción del envío).
            }

            return ex;
        }
    }
}
