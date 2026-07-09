namespace Reservas.Domain.Reservas;

/// <summary>
/// Slot de inventario: una fila por (habitación, noche). La unicidad de <c>(HabitacionId, Noche)</c>
/// —clave clustered en el motor— ES el mecanismo anti-overbooking (ADR-016): impide dos reservas
/// de la misma noche. Rango semiabierto <c>[entrada, salida)</c>, por eso la noche de salida no se ocupa.
/// </summary>
public sealed class NocheHabitacion
{
    public Guid HabitacionId { get; private set; }
    public DateOnly Noche { get; private set; }
    public Guid ReservaId { get; private set; }

    private NocheHabitacion() { } // EF Core

    public NocheHabitacion(Guid habitacionId, DateOnly noche, Guid reservaId)
    {
        HabitacionId = habitacionId;
        Noche = noche;
        ReservaId = reservaId;
    }
}
