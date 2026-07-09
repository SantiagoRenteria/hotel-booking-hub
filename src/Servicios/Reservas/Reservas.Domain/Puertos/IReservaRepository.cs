using Reservas.Domain.Reservas;

namespace Reservas.Domain.Puertos;

/// <summary>
/// Puerto de persistencia de reservas (el dominio define el puerto; el adaptador EF Core vive en
/// infraestructura). El invariante anti-overbooking lo garantiza el motor, no la aplicación.
/// </summary>
public interface IReservaRepository
{
    /// <summary>
    /// STAGEA la reserva y sus slots en el <c>DbContext</c> (<c>Add</c>), sin guardar ni abrir transacción.
    /// El <c>EjecutorTransaccional</c> (usado por el <c>TransactionBehavior</c> y por los tests de persistencia)
    /// hace el único <c>SaveChanges</c> + commit, de modo que dominio y outbox se escriben atómicamente.
    /// El árbitro anti-overbooking (<c>UNIQUE(HabitacionId, Noche)</c>) actúa en ese INSERT; la violación
    /// 2627/2601 se traduce a <see cref="HabitacionNoDisponibleException"/> allí, y el deadlock 1205 se reintenta.
    /// </summary>
    void Agregar(Reserva reserva);
}
