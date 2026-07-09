namespace Reservas.Domain.Reservas;

/// <summary>
/// Estado del ciclo de vida de una reserva. En la Story 1.5 solo existe <see cref="Confirmada"/>;
/// los estados de cancelación (<c>CancelacionSolicitada</c>, <c>Cancelada</c>) se añaden en la Épica 4.
/// </summary>
public enum EstadoReserva
{
    Confirmada = 1,
}
