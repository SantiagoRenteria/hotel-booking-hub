namespace HotelBookingHub.Comun.Eventos;

/// <summary>
/// Carga útil del evento de catálogo <c>HabitacionDeshabilitada.v1</c> que produce el BC de Hoteles cuando una
/// habitación se deshabilita (Story 2.5). La proyección de E3 la retirará de la oferta.
///
/// Order key = (<see cref="AggregateId"/> = HabitacionId, <see cref="EventoIntegracion.Version"/>).
/// </summary>
public sealed record HabitacionDeshabilitadaV1(
    Guid AggregateId,        // = HabitacionId; componente de la order key.
    Guid HotelId)
{
    /// <summary>Tipo del evento (PascalCase español + semver). Va en <see cref="EventoIntegracion.Type"/>.</summary>
    public const string Tipo = "HabitacionDeshabilitada.v1";
}
