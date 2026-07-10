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
    /// <c>trace-id</c> de negocio del envelope (STUB — se implementa en la fase verde).
    /// </summary>
    public static Activity? IniciarConsumo(string nombre, string? traceIdNegocio) => null;
}
