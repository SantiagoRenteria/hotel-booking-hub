namespace HotelBookingHub.Comun.Eventos;

/// <summary>
/// Carga útil del evento de catálogo <c>HotelHabilitado.v1</c> que produce el BC de Hoteles cuando un hotel se
/// (re)habilita (Story 3.2). La proyección de E3 levanta la dimensión de estado de hotel; las habitaciones del
/// hotel vuelven a ofertarse <b>respetando su estado individual</b> (una habitación deshabilitada por sí misma
/// sigue oculta), porque el estado de hotel y el de la habitación son zonas independientes (field-ownership).
///
/// Order key = (<see cref="AggregateId"/> = HotelId, <see cref="EventoIntegracion.Version"/>).
/// </summary>
public sealed record HotelHabilitadoV1(
    Guid AggregateId,        // = HotelId; componente de la order key.
    string Ciudad)
{
    /// <summary>Tipo del evento (PascalCase español + semver). Va en <see cref="EventoIntegracion.Type"/>.</summary>
    public const string Tipo = "HotelHabilitado.v1";
}
