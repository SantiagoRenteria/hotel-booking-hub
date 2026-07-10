using HotelBookingHub.Comun.Excepciones;
using HotelBookingHub.Comun.Resultados;

namespace Reservas.Domain.Reservas;

/// <summary>
/// Se lanza cuando se intenta una transición de estado no permitida sobre una <see cref="Reserva"/>
/// (p. ej. solicitar la cancelación de una reserva que ya no está <see cref="EstadoReserva.Confirmada"/>,
/// o de una estancia ya iniciada). Es un desenlace de negocio esperado → <see cref="EstadoResultado.Conflicto"/>
/// → <b>409 Conflict</b> en el borde HTTP (misma familia que <see cref="HabitacionNoDisponibleException"/> y
/// coherente con <see cref="EstanciaInvalidaException"/>: guard por excepción de dominio, no <c>Result</c>,
/// decisión party-mode Story 4.1 Task 0). El guard vive DENTRO del método del agregado.
/// </summary>
public sealed class TransicionEstadoInvalidaException : ExcepcionNegocio
{
    /// <summary>Estado en el que estaba la reserva al intentar la transición.</summary>
    public EstadoReserva Origen { get; }

    /// <summary>Estado destino que se intentó alcanzar.</summary>
    public EstadoReserva Destino { get; }

    public TransicionEstadoInvalidaException(EstadoReserva origen, EstadoReserva destino, string? motivo = null)
        : base(
            EstadoResultado.Conflicto,
            motivo ?? $"No se puede pasar la reserva de '{origen}' a '{destino}'.")
    {
        Origen = origen;
        Destino = destino;
    }
}
