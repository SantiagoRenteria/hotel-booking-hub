namespace HotelBookingHub.Comun.Eventos;

/// <summary>
/// Carga útil (data) del evento <c>ReservaCancelada.v1</c> que produce el BC de Reservas al APROBAR una
/// cancelación (Story 4.2): la reserva quedó <c>Cancelada</c> y se liberó el inventario. La Épica 4 define y
/// prueba su contrato; E5 (notificaciones) lo consumirá. Order key = (<see cref="AggregateId"/> = <c>ReservaId</c>,
/// <see cref="EventoIntegracion.Version"/>). La penalidad viaja como la EFECTIVAMENTE aplicada (no la sugerida).
/// </summary>
public sealed record ReservaCanceladaV1(
    Guid AggregateId,                     // = ReservaId (UUID v7); componente de la order key.
    string ResueltaPor,
    DateOnly FechaResolucion,
    decimal PenalidadAplicadaPorcentaje,  // efectivamente aplicada (0 si se condonó).
    bool PenalidadFueOverride,            // true si el agente sobrescribió la sugerida congelada.
    // Enriquecimiento aditivo (Story 5.3, party-mode opción a): destinatario del correo de resolución (viajero).
    // Nullable por fidelidad al dominio; el consumidor omite el envío si es nulo. Solo se notifica al viajero (AC).
    string? HuespedEmail = null)
{
    /// <summary>Tipo del evento (PascalCase español + semver). Va en <see cref="EventoIntegracion.Type"/>.</summary>
    public const string Tipo = "ReservaCancelada.v1";
}
