using Reservas.Domain.Reservas;

namespace Reservas.Domain.Puertos;

/// <summary>
/// Puerto de persistencia de reservas (el dominio define el puerto; el adaptador EF Core vive en
/// infraestructura). El invariante anti-overbooking lo garantiza el motor, no la aplicación.
/// </summary>
public interface IReservaRepository
{
    /// <summary>
    /// Persiste la reserva y sus slots en una transacción (READ COMMITTED). Si algún slot ya está
    /// ocupado, lanza <see cref="HabitacionNoDisponibleException"/> (arbitrado por el índice único).
    /// El deadlock (1205) se propaga para que la política de reintentos lo reejecute.
    /// </summary>
    Task ConfirmarAsync(Reserva reserva, CancellationToken ct);
}
