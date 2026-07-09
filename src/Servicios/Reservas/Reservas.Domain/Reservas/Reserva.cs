namespace Reservas.Domain.Reservas;

/// <summary>
/// Aggregate root de una reserva confirmada. Identidad = <b>UUID v7</b> (PK no-clustered; la
/// clustering key <c>Seq</c> es shadow property configurada en infraestructura — ADR-017).
/// Genera sus <see cref="NocheHabitacion"/> (slots de inventario) para toda la estancia; la
/// unicidad de esos slots garantiza el invariante anti-overbooking en el motor.
/// </summary>
public sealed class Reserva
{
    private readonly List<NocheHabitacion> _noches = [];

    public Guid Id { get; private set; }
    public Guid HabitacionId { get; private set; }
    public Estancia Estancia { get; private set; } = null!;
    public EstadoReserva Estado { get; private set; }

    /// <summary>Slots de inventario que ocupa la reserva (una fila por noche del rango).</summary>
    public IReadOnlyCollection<NocheHabitacion> Noches => _noches.AsReadOnly();

    private Reserva() { } // EF Core

    /// <summary>Crea una reserva confirmada y sus slots para el rango <c>[entrada, salida)</c>.</summary>
    public static Reserva Crear(Guid habitacionId, Estancia estancia)
    {
        var reserva = new Reserva
        {
            Id = Guid.CreateVersion7(),
            HabitacionId = habitacionId,
            Estancia = estancia,
            Estado = EstadoReserva.Confirmada,
        };

        // Slots en orden determinístico ascendente por noche (minimiza deadlocks bajo concurrencia).
        for (var noche = estancia.Entrada; noche < estancia.Salida; noche = noche.AddDays(1))
        {
            reserva._noches.Add(new NocheHabitacion(habitacionId, noche, reserva.Id));
        }

        return reserva;
    }
}
