namespace HotelBookingHub.Comun.Eventos;

/// <summary>
/// Carga útil del evento de catálogo <c>HotelDeshabilitado.v1</c> que produce el BC de Hoteles cuando un hotel
/// se deshabilita (Story 3.2, cierre de AC-E3.2.2/FR-7). La proyección de E3 lo aplica a una <b>dimensión
/// independiente</b> de estado de hotel (no cascada a las habitaciones): la búsqueda excluye las habitaciones
/// cuyo hotel esté inactivo, preservando el estado individual de cada habitación.
///
/// Order key = (<see cref="AggregateId"/> = HotelId, <see cref="EventoIntegracion.Version"/>). Lleva
/// <see cref="Ciudad"/> para que el consumidor pueda invalidar la caché de búsqueda de esa ciudad.
/// </summary>
public sealed record HotelDeshabilitadoV1(
    Guid AggregateId,        // = HotelId; componente de la order key.
    string Ciudad)
{
    /// <summary>Tipo del evento (PascalCase español + semver). Va en <see cref="EventoIntegracion.Type"/>.</summary>
    public const string Tipo = "HotelDeshabilitado.v1";
}
