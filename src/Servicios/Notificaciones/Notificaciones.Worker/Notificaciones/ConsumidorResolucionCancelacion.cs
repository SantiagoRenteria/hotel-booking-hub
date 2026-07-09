using System.Globalization;
using System.Text.Json;
using HotelBookingHub.Comun.Eventos;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Consumidor de la resolución de una cancelación (Story 5.3): reacciona a <c>ReservaCancelada.v1</c> (aprobación/
/// condonación, AC-E5.3.1) y a <c>SolicitudCancelacionRechazada.v1</c> (rechazo, AC-E5.3.2), notificando al
/// viajero el desenlace FINAL. <b>Invariante</b> (contrato de 4.2): un rechazo NUNCA comunica cancelación y
/// viceversa — se garantiza despachando por <see cref="EventoIntegracion.Type"/>. Reutiliza
/// <see cref="INotificador"/> + inbox idempotente vía <see cref="EnvioIdempotenteCorreos"/> (dedup por
/// <c>(MessageId, version, "viajero")</c>).
/// </summary>
public sealed class ConsumidorResolucionCancelacion(INotificador notificador, IInboxIdempotencia inbox) : IProcesadorEvento
{
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);
    private readonly EnvioIdempotenteCorreos _envio = new(notificador, inbox);

    public async Task ProcesarAsync(EventoIntegracion evento, CancellationToken ct)
    {
        var correo = evento.Type switch
        {
            ReservaCanceladaV1.Tipo => CorreoAprobacion(Payload<ReservaCanceladaV1>(evento)),
            SolicitudCancelacionRechazadaV1.Tipo => CorreoRechazo(Payload<SolicitudCancelacionRechazadaV1>(evento)),
            _ => null, // otros eventos se ignoran silenciosamente.
        };

        if (correo is null)
        {
            return;
        }

        await _envio.EnviarLoteAsync(evento, [correo], ct);
    }

    /// <summary>AC-E5.3.1: penalidad FINAL aplicada, o aviso de condonación. Se distingue la <b>condonación</b> (el
    /// agente perdonó una penalidad que aplicaba → <c>PenalidadFueOverride</c>) del <b>0% natural</b> (nunca hubo
    /// penalidad, p. ej. cancelación con suficiente antelación): en el dominio <c>override == true</c> solo ocurre
    /// al condonar una sugerida &gt; 0, así que ambos casos se resuelven con el flag.</summary>
    private static Correo CorreoAprobacion(ReservaCanceladaV1 data)
    {
        string cuerpo;
        if (data.PenalidadAplicadaPorcentaje == 0m)
        {
            cuerpo = data.PenalidadFueOverride
                ? "Tu cancelación fue aprobada y el agente CONDONÓ la penalidad (0%). No se te cobrará penalidad."
                : "Tu cancelación fue aprobada. No aplica penalidad (0%).";
        }
        else
        {
            var pct = data.PenalidadAplicadaPorcentaje.ToString("0.##", CultureInfo.InvariantCulture);
            cuerpo = $"Tu cancelación fue aprobada. Penalidad FINAL aplicada: {pct}%.";
        }

        return new Correo("viajero", data.HuespedEmail, "Tu cancelación fue resuelta", cuerpo);
    }

    /// <summary>AC-E5.3.2: mensaje inequívoco — la reserva sigue Confirmada + el motivo. NO comunica cancelación.</summary>
    private static Correo CorreoRechazo(SolicitudCancelacionRechazadaV1 data) =>
        new(
            Efecto: "viajero",
            Destinatario: data.HuespedEmail,
            Asunto: "Tu solicitud de cancelación fue rechazada",
            Cuerpo: $"Tu solicitud de cancelación fue rechazada; tu reserva sigue CONFIRMADA. Motivo: {data.MotivoRechazo}");

    private static T Payload<T>(EventoIntegracion evento)
    {
        // Tras el transporte, Data llega como JsonElement (no como el tipo concreto): se deserializa aquí.
        if (evento.Data is T tipado)
        {
            return tipado;
        }

        var json = JsonSerializer.Serialize(evento.Data, _opciones);
        return JsonSerializer.Deserialize<T>(json, _opciones)
            ?? throw new InvalidOperationException(
                $"Evento {evento.Id} ({evento.Type}) sin data deserializable a {typeof(T).Name}.");
    }
}
