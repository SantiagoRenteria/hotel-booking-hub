using System.Globalization;
using System.Text.Json;
using HotelBookingHub.Comun.Eventos;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Consumidor del evento <c>ReservaConfirmada.v1</c> (Story 5.1a). Recibe el envelope
/// <see cref="EventoIntegracion"/> tal cual (el <c>data</c> puede llegar como <c>JsonElement</c> tras el
/// transporte, patrón del consumidor de catálogo de 3.1) y emite DOS correos: uno al huésped y otro al agente.
/// Cada correo es un efecto idempotente independiente vía <see cref="IInboxIdempotencia"/> (Story 5.1b): dedup
/// por <c>(MessageId, version, destinatario)</c>, con liberación de compensación si el envío falla.
/// </summary>
public sealed class ConsumidorReservaConfirmada(INotificador notificador, IInboxIdempotencia inbox) : IProcesadorEvento
{
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);

    public async Task ProcesarAsync(EventoIntegracion evento, CancellationToken ct)
    {
        // Solo reacciona a la confirmación de reserva; otros eventos se ignoran silenciosamente.
        if (evento.Type != ReservaConfirmadaV1.Tipo)
        {
            return;
        }

        // Guard: un payload malformado/nulo se rechaza con un error DESCRIPTIVO (Id/Type), no con un NRE opaco.
        // La política de reintento/dead-letter ante veneno es de 5.1b.
        var data = Deserializar(evento)
            ?? throw new InvalidOperationException(
                $"Evento {evento.Id} ({evento.Type}) sin data deserializable a {nameof(ReservaConfirmadaV1)}.");

        var estancia = $"{data.Entrada:yyyy-MM-dd} → {data.Salida:yyyy-MM-dd}";
        // InvariantCulture: el importe del correo NO debe variar con la config regional del host.
        var total = data.PrecioTotal.ToString("0.00", CultureInfo.InvariantCulture);

        // Cada correo es un efecto INDEPENDIENTE deduplicado por su propia clave (MessageId, version, destinatario).
        // Se intentan AMBOS aunque uno falle (F2): un destinatario enfermo (dirección inválida) no debe impedir el
        // envío al sano. Los fallos se agregan y se propagan al final para que el transporte re-entregue
        // (at-least-once); cada efecto ya confirmado se descarta por su reserva → sin duplicado, sin pérdida.
        var falloHuesped = await IntentarEnviarAsync(
            evento, efecto: "huesped", destinatario: data.HuespedEmail,
            asunto: $"Reserva confirmada en {data.HotelNombre}",
            cuerpo: $"Hola {data.HuespedNombre}, tu reserva en {data.HotelNombre} ({data.Ciudad}) está confirmada para {estancia}. Total: {total}.",
            ct);

        var falloAgente = await IntentarEnviarAsync(
            evento, efecto: "agente", destinatario: data.AgenteEmail,
            asunto: $"Reserva confirmada: {data.HotelNombre}",
            cuerpo: $"La reserva de {data.HuespedNombre} en {data.HotelNombre} ({data.Ciudad}) para {estancia} quedó confirmada. Total: {total}.",
            ct);

        var fallos = new[] { falloHuesped, falloAgente }.OfType<Exception>().ToList();
        if (fallos.Count == 1)
        {
            throw fallos[0]; // preserva el tipo original (útil para clasificación de veneno aguas arriba).
        }

        if (fallos.Count > 1)
        {
            throw new AggregateException("Falló el envío de la confirmación a uno o más destinatarios.", fallos);
        }
    }

    /// <summary>
    /// Envía un correo <b>exactamente una vez</b> bajo re-entrega (AC-E5.1b.1): reserva atómica de la clave del
    /// efecto (<c>SETNX</c>); si ya estaba reservada → duplicado, se descarta; si gana la reserva → envía y, si el
    /// envío falla, LIBERA la reserva para que la re-entrega at-least-once reintente sin duplicar (sin pérdida).
    /// Devuelve la excepción del envío (o <c>null</c> si tuvo éxito / se descartó por dedup), sin propagarla: el
    /// llamador decide qué hacer tras intentar TODOS los efectos.
    /// </summary>
    private async Task<Exception?> IntentarEnviarAsync(
        EventoIntegracion evento, string efecto, string destinatario, string asunto, string cuerpo, CancellationToken ct)
    {
        if (!await inbox.IntentarMarcarProcesadoAsync(evento.Id, evento.Version, efecto, ct))
        {
            return null; // Efecto ya realizado (o en curso) → dedup: no se duplica.
        }

        try
        {
            await notificador.NotificarAsync(destinatario, asunto, cuerpo, ct);
            return null;
        }
        catch (Exception ex)
        {
            // El efecto no llegó a producirse: liberar la reserva para que el reintento lo complete. La liberación
            // es de COMPENSACIÓN y usa CancellationToken.None (F1): un ct cancelado (apagado graceful) NO debe
            // impedir liberar, o el correo se perdería hasta que expire el TTL. Best-effort: si la liberación falla
            // (p. ej. Redis caído) NO se enmascara la excepción original del envío; el TTL recupera la reserva.
            try
            {
                await inbox.LiberarAsync(evento.Id, evento.Version, efecto, CancellationToken.None);
            }
            catch (Exception exLiberar) when (exLiberar is not OperationCanceledException)
            {
                // Liberación best-effort; se ignora para no ocultar la causa real (la excepción del envío).
            }

            return ex;
        }
    }

    /// <summary>Tras el transporte, <c>Data</c> llega como <c>JsonElement</c>; en invocación directa como el tipo.
    /// Devuelve <c>null</c> si el payload no es deserializable (el llamador decide cómo tratarlo).</summary>
    private static ReservaConfirmadaV1? Deserializar(EventoIntegracion evento) =>
        evento.Data is ReservaConfirmadaV1 tipado
            ? tipado
            : JsonSerializer.Deserialize<ReservaConfirmadaV1>(JsonSerializer.Serialize(evento.Data, _opciones), _opciones);
}
