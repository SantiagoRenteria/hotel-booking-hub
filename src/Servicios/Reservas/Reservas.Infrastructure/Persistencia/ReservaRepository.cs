using Microsoft.EntityFrameworkCore;
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

    // Tracking por defecto: la entidad cargada se modifica en el handler y el EjecutorTransaccional la persiste
    // en el mismo SaveChanges. La owned SolicitudCancelacion viaja en la misma fila (owned reference).
    public Task<Reserva?> ObtenerAsync(Guid id, CancellationToken ct) =>
        db.Reservas.FirstOrDefaultAsync(r => r.Id == id, ct);

    // Incluye los slots para que Reserva.Resolver pueda liberarlos (borrado de huérfanos) en la misma tx.
    public Task<Reserva?> ObtenerConNochesAsync(Guid id, CancellationToken ct) =>
        db.Reservas.Include(r => r.Noches).FirstOrDefaultAsync(r => r.Id == id, ct);
}
