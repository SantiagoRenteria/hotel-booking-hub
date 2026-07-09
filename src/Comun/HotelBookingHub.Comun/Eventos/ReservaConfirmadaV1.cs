namespace HotelBookingHub.Comun.Eventos;

/// <summary>
/// Carga útil (data) del evento de integración <c>ReservaConfirmada.v1</c> que produce el BC de
/// Reservas y consume <c>Notificaciones.Worker</c> (correo) y la proyección de disponibilidad.
///
/// Contrato congelado (Story 1.3):
/// - <b>Dedup key</b> = <see cref="EventoIntegracion.Id"/> (MessageId del envelope) → la consume E5.
/// - <b>Order key</b> = (<see cref="AggregateId"/>, <see cref="EventoIntegracion.Version"/>) → la consume E3.
/// - <see cref="EventoIntegracion.Type"/> = <see cref="Tipo"/> con semver en el sufijo (<c>.v1</c>).
///
/// Formato (System.Text.Json): camelCase; <see cref="DateOnly"/> como <c>yyyy-MM-dd</c>; dinero en
/// <see cref="decimal"/>. NO se expone ningún identificador interno (p. ej. <c>Seq</c>).
/// </summary>
public sealed record ReservaConfirmadaV1(
    Guid AggregateId,        // = ReservaId (UUID v7); componente de la order key.
    Guid HotelId,
    string HotelNombre,
    string Ciudad,
    Guid HabitacionId,
    DateOnly Entrada,
    DateOnly Salida,
    string HuespedNombre,
    string HuespedEmail,
    string AgenteEmail,
    decimal PrecioTotal)
{
    /// <summary>Tipo del evento (PascalCase español + semver). Va en <see cref="EventoIntegracion.Type"/>.</summary>
    public const string Tipo = "ReservaConfirmada.v1";
}
