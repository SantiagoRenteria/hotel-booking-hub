using Reservas.Domain.Puertos;
using Reservas.Domain.Reservas;

namespace Reservas.Infrastructure.Persistencia;

/// <summary>
/// Adaptador EF Core del puerto <see cref="IReservaRepository"/>. Solo STAGEA (Add) la reserva y sus slots
/// en el <c>DbContext</c>; la transacción, el <c>SaveChanges</c> único (dominio + outbox) y la clasificación
/// del error los aporta el <see cref="EjecutorTransaccional"/>. El índice único <c>UNIQUE(HabitacionId, Noche)</c>
/// arbitra el conflicto en el propio INSERT (ADR-016), NO la aplicación.
/// </summary>
public sealed class ReservaRepository(ReservasDbContext db) : IReservaRepository
{
    public void Agregar(Reserva reserva) => db.Reservas.Add(reserva);
}
