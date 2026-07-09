namespace Reservas.Domain.Reservas;

/// <summary>
/// Desenlace de una solicitud de cancelación resuelta (Story 4.2): aprobada (la reserva quedó
/// <see cref="EstadoReserva.Cancelada"/> y se liberó el inventario) o rechazada (volvió a
/// <see cref="EstadoReserva.Confirmada"/>). Se persiste en la <see cref="SolicitudCancelacion"/> para auditoría.
/// </summary>
public enum ResultadoResolucion
{
    Aprobada = 1,
    Rechazada = 2,
}
