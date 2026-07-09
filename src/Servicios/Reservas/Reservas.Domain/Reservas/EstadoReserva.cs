namespace Reservas.Domain.Reservas;

/// <summary>
/// Estado del ciclo de vida de una reserva. En la Story 1.5 solo existía <see cref="Confirmada"/>;
/// la Épica 4 añade los estados de cancelación. Transiciones legales (guard en <see cref="Reserva"/>):
/// <c>Confirmada → CancelacionSolicitada</c> (Story 4.1) y <c>CancelacionSolicitada → Cancelada</c>
/// (aprobación, Story 4.2). Cualquier otra transición lanza <see cref="TransicionEstadoInvalidaException"/> (409).
/// </summary>
public enum EstadoReserva
{
    Confirmada = 1,
    CancelacionSolicitada = 2,
    Cancelada = 3,
}
