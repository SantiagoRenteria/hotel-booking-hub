namespace HotelBookingHub.Comun.Eventos;

/// <summary>
/// Carga útil del evento de catálogo <c>PrecioHabitacionCambiado.v1</c> que produce el BC de Hoteles cuando
/// cambia el costo base o los impuestos de una habitación (Story 2.5, AC-E2.5.3 — solo si el precio cambió).
///
/// Order key = (<see cref="AggregateId"/> = HabitacionId, <see cref="EventoIntegracion.Version"/>).
/// Formato: camelCase; dinero en <see cref="decimal"/>.
/// </summary>
public sealed record PrecioHabitacionCambiadoV1(
    Guid AggregateId,        // = HabitacionId; componente de la order key.
    Guid HotelId,
    decimal CostoBase,
    decimal Impuestos)
{
    /// <summary>Tipo del evento (PascalCase español + semver). Va en <see cref="EventoIntegracion.Type"/>.</summary>
    public const string Tipo = "PrecioHabitacionCambiado.v1";
}
