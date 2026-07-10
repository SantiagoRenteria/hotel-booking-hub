using System.Diagnostics;

namespace HotelBookingHub.Comun.Observabilidad;

/// <summary>
/// <c>ActivitySource</c> compartido del sistema (arquitectura: "ActivitySource compartido HotelBookingHub").
/// Todos los servicios emiten sus spans de negocio desde este source único; <c>ServiceDefaults</c> lo registra
/// con <c>AddSource("HotelBookingHub")</c> para exportarlo al dashboard de Aspire (Story 7.1).
/// </summary>
public static class ActividadHotelBookingHub
{
    /// <summary>Nombre canónico del source (debe coincidir con el <c>AddSource</c> de ServiceDefaults).</summary>
    public const string NombreSource = "HotelBookingHub";

    public static readonly ActivitySource Source = new(NombreSource);

    /// <summary>
    /// Inicia el span del consumidor de un evento correlacionándolo con la traza originante mediante el
    /// <c>trace-id</c> de negocio del envelope: el span cuelga de la MISMA traza (mismo <c>TraceId</c> en el
    /// dashboard). El transporte Dapr está diferido, así que el traceparent completo (con el span-id padre real)
    /// no viaja; se reconstruye un padre con el trace-id conocido y un span-id sintético. Cuando se cablee Dapr,
    /// el sidecar propaga el traceparent completo y esta correlación manual es innecesaria.
    /// </summary>
    public static Activity? IniciarConsumo(string nombre, string? traceIdNegocio)
    {
        var contexto = ContextoDesde(traceIdNegocio);
        return contexto is { } padre
            ? Source.StartActivity(nombre, ActivityKind.Consumer, padre)
            : Source.StartActivity(nombre, ActivityKind.Consumer);
    }

    /// <summary>
    /// Reconstruye un <see cref="ActivityContext"/> a partir del valor de correlación del envelope. Acepta tanto
    /// un <c>traceparent</c> W3C completo (<c>00-&lt;trace&gt;-&lt;span&gt;-&lt;flags&gt;</c>) como un trace-id
    /// de 32 hex (formato de <c>Activity.TraceId.ToString()</c>). Devuelve <c>null</c> si el valor es vacío o inválido.
    /// </summary>
    internal static ActivityContext? ContextoDesde(string? traceIdNegocio)
    {
        if (string.IsNullOrWhiteSpace(traceIdNegocio))
        {
            return null;
        }

        // El padre "vino por el cable": se marca Recorded (para que el span del consumidor SIEMPRE se muestree,
        // incluso si el productor mandó un traceparent con flag 00) e isRemote (semántica de contexto remoto).
        // Normalizar la flag evita que la rama traceparent y la de 32-hex se comporten distinto bajo un sampler
        // ParentBased en producción (asimetría que perdía el span en silencio).
        const ActivityTraceFlags flagsCorrelacion = ActivityTraceFlags.Recorded;

        // Traceparent W3C completo → se respeta el trace-id + span-id reales; solo se normaliza la flag.
        if (ActivityContext.TryParse(traceIdNegocio, traceState: null, out var contextoCompleto))
        {
            return new ActivityContext(
                contextoCompleto.TraceId, contextoCompleto.SpanId, flagsCorrelacion,
                contextoCompleto.TraceState, isRemote: true);
        }

        // Trace-id de 32 hex → padre sintético (span-id aleatorio; el traceparent completo lo aportaría Dapr).
        try
        {
            var traceId = ActivityTraceId.CreateFromString(traceIdNegocio.AsSpan());
            return new ActivityContext(traceId, ActivitySpanId.CreateRandom(), flagsCorrelacion, isRemote: true);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null; // Ni traceparent ni trace-id de 32 hex → sin correlación (span raíz).
        }
    }
}
