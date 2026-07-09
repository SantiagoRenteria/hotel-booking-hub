namespace HotelBookingHub.Comun.Eventos;

/// <summary>
/// Carga útil (data) del evento de integración <c>SolicitudCancelacionRegistrada.v1</c> que produce el BC de
/// Reservas al registrar una solicitud de cancelación (Story 4.1). La Épica 4 <b>define y prueba</b> su contrato
/// aunque E5 (notificaciones) aún no lo consuma (regla de propiedad de eventos, party-mode Winston).
///
/// Contrato congelado (Story 4.1):
/// - <b>Dedup key</b> = <see cref="EventoIntegracion.Id"/> (MessageId del envelope).
/// - <b>Order key</b> = (<see cref="AggregateId"/> = <c>ReservaId</c>, <see cref="EventoIntegracion.Version"/>):
///   todos los eventos de una misma reserva se ordenan por su versión.
/// - <see cref="EventoIntegracion.Type"/> = <see cref="Tipo"/> con semver en el sufijo (<c>.v1</c>).
///
/// Formato (System.Text.Json): camelCase; <see cref="DateOnly"/> como <c>yyyy-MM-dd</c>; la penalidad como
/// número decimal (porcentaje SUGERIDO y congelado, no un cobro).
/// </summary>
public sealed record SolicitudCancelacionRegistradaV1(
    Guid AggregateId,             // = ReservaId (UUID v7); componente de la order key.
    string Iniciador,             // "Viajero" | "Agente".
    string MotivoCategoria,
    string MotivoDetalle,
    decimal PenalidadPorcentaje,  // sugerida y congelada en la fecha de solicitud (0..100).
    DateOnly FechaSolicitud)
{
    /// <summary>Tipo del evento (PascalCase español + semver). Va en <see cref="EventoIntegracion.Type"/>.</summary>
    public const string Tipo = "SolicitudCancelacionRegistrada.v1";
}
