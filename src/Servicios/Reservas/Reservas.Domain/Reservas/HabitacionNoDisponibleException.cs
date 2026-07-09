namespace Reservas.Domain.Reservas;

/// <summary>
/// Se lanza cuando una reserva pierde la carrera por un slot ya ocupado (violación del índice único
/// <c>UNIQUE(HabitacionId, Noche)</c>, <c>SqlException.Number</c> 2627/2601). Es un desenlace de negocio
/// esperado bajo concurrencia → se traduce a <b>409 Conflict</b> en el borde HTTP (Story 1.6a). Sin retry.
/// </summary>
public sealed class HabitacionNoDisponibleException : Exception
{
    public HabitacionNoDisponibleException()
        : base("La habitación no está disponible para las fechas seleccionadas") { }

    public HabitacionNoDisponibleException(string message) : base(message) { }

    public HabitacionNoDisponibleException(string message, Exception innerException)
        : base(message, innerException) { }
}
