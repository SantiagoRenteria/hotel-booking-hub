using HotelBookingHub.Comun.Excepciones;
using HotelBookingHub.Comun.Resultados;

namespace Reservas.Domain.Reservas;

/// <summary>
/// Se lanza cuando una reserva pierde la carrera por un slot ya ocupado (violación del índice único
/// <c>UNIQUE(HabitacionId, Noche)</c>, <c>SqlException.Number</c> 2627/2601). Es un desenlace de negocio
/// esperado bajo concurrencia → <see cref="EstadoResultado.Conflicto"/> → <b>409 Conflict</b> en el borde HTTP
/// (Story 1.6a). Sin retry. Desde 2.2 hereda de <see cref="ExcepcionNegocio"/> para reutilizar el handler
/// transversal (mismo mapeo que el patrón Result); el contrato observable (409) no cambia.
/// </summary>
public sealed class HabitacionNoDisponibleException : ExcepcionNegocio
{
    public HabitacionNoDisponibleException()
        : base(EstadoResultado.Conflicto, "La habitación no está disponible para las fechas seleccionadas") { }

    public HabitacionNoDisponibleException(string message)
        : base(EstadoResultado.Conflicto, message) { }

    public HabitacionNoDisponibleException(string message, Exception innerException)
        : base(EstadoResultado.Conflicto, message, innerException) { }
}
