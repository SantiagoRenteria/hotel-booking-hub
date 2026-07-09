namespace HotelBookingHub.Comun.Eventos;

/// <summary>
/// Carga útil (data) del evento <c>SolicitudCancelacionRechazada.v1</c> que produce el BC de Reservas al RECHAZAR
/// una cancelación (Story 4.2): la reserva vuelve a <c>Confirmada</c> y NO se libera inventario. La Épica 4 define
/// y prueba su contrato; E5 lo consumirá. Order key = (<see cref="AggregateId"/> = <c>ReservaId</c>,
/// <see cref="EventoIntegracion.Version"/>).
/// </summary>
public sealed record SolicitudCancelacionRechazadaV1(
    Guid AggregateId,          // = ReservaId (UUID v7); componente de la order key.
    string ResueltaPor,
    DateOnly FechaResolucion,
    string MotivoRechazo)
{
    /// <summary>Tipo del evento (PascalCase español + semver). Va en <see cref="EventoIntegracion.Type"/>.</summary>
    public const string Tipo = "SolicitudCancelacionRechazada.v1";
}
