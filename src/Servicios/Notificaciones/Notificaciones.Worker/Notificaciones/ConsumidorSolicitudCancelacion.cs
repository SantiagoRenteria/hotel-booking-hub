using System.Globalization;
using System.Text.Json;
using HotelBookingHub.Comun.Eventos;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Consumidor de <c>SolicitudCancelacionRegistrada.v1</c> (Story 5.2, AC-E5.2.1): al registrarse una solicitud
/// de cancelación, avisa al <b>viajero</b> con un acuse que incluye la penalidad ETIQUETADA como estimación (no
/// cobro final; el cobro definitivo llega en 5.3) y al <b>agente</b> con un aviso de "por resolver". Reutiliza
/// <see cref="INotificador"/> (5.1a) y el inbox idempotente (5.1b) vía <see cref="EnvioIdempotenteCorreos"/>:
/// cada correo se deduplica por <c>(MessageId, version, destinatario)</c>.
/// </summary>
public sealed class ConsumidorSolicitudCancelacion(INotificador notificador, IInboxIdempotencia inbox) : IProcesadorEvento
{
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);
    private readonly EnvioIdempotenteCorreos _envio = new(notificador, inbox);

    public async Task ProcesarAsync(EventoIntegracion evento, CancellationToken ct)
    {
        if (evento.Type != SolicitudCancelacionRegistradaV1.Tipo)
        {
            return; // otros eventos se ignoran silenciosamente.
        }

        var data = Deserializar(evento)
            ?? throw new InvalidOperationException(
                $"Evento {evento.Id} ({evento.Type}) sin data deserializable a {nameof(SolicitudCancelacionRegistradaV1)}.");

        // InvariantCulture: ni el porcentaje ni la fecha del correo deben variar con la config regional del host
        // (una cultura no gregoriana/dígitos no-ASCII alteraría el año o los dígitos).
        var penalidad = data.PenalidadPorcentaje.ToString("0.##", CultureInfo.InvariantCulture);
        var fecha = data.FechaSolicitud.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var correos = new List<Correo>
        {
            // Viajero: acuse con la penalidad ETIQUETADA como estimación (congelada en la fecha de solicitud), NO el
            // cobro final — la resolución definitiva se notifica en 5.3.
            new(
                Efecto: "viajero",
                Destinatario: data.HuespedEmail,
                Asunto: "Recibimos tu solicitud de cancelación",
                Cuerpo: $"Registramos tu solicitud de cancelación del {fecha}. " +
                        $"La penalidad ESTIMADA es del {penalidad}% (estimación sujeta a resolución; el importe definitivo " +
                        "se confirmará cuando se resuelva). Te avisaremos entonces."),

            // Agente: aviso de que hay una solicitud por resolver.
            new(
                Efecto: "agente",
                Destinatario: data.AgenteEmail,
                Asunto: "Solicitud de cancelación por resolver",
                Cuerpo: $"Hay una solicitud de cancelación (iniciada por {data.Iniciador}) por resolver. " +
                        $"Penalidad estimada: {penalidad}%. Motivo: {data.MotivoCategoria}."),
        };

        await _envio.EnviarLoteAsync(evento, correos, ct);
    }

    /// <summary>Tras el transporte, <c>Data</c> llega como <c>JsonElement</c>; en invocación directa como el tipo.</summary>
    private static SolicitudCancelacionRegistradaV1? Deserializar(EventoIntegracion evento) =>
        evento.Data is SolicitudCancelacionRegistradaV1 tipado
            ? tipado
            : JsonSerializer.Deserialize<SolicitudCancelacionRegistradaV1>(
                JsonSerializer.Serialize(evento.Data, _opciones), _opciones);
}
