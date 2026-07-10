namespace Reservas.Domain.Reservas;

/// <summary>
/// Decisión del agente al resolver una solicitud de cancelación (Story 4.2). Aprobar cancela la reserva y libera
/// el inventario; condonar es aprobar renunciando a la penalidad congelada (override a 0%); rechazar devuelve la
/// reserva a <see cref="EstadoReserva.Confirmada"/> sin tocar slots. La penalidad NUNCA se recalcula: aplicar usa
/// la sugerida congelada (Story 4.1); condonar la fija en 0.
/// </summary>
public enum DecisionCancelacion
{
    AprobarAplicandoPenalidad = 1,
    AprobarCondonandoPenalidad = 2,
    Rechazar = 3,
}
