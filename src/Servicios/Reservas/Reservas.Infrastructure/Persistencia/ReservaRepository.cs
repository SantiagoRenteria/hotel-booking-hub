using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Reservas.Domain.Puertos;
using Reservas.Domain.Reservas;

namespace Reservas.Infrastructure.Persistencia;

/// <summary>
/// Adaptador EF Core del puerto <see cref="IReservaRepository"/>. Persiste la reserva y sus slots en
/// una transacción READ COMMITTED; el índice único <c>UNIQUE(HabitacionId, Noche)</c> arbitra el
/// conflicto en el propio INSERT (ADR-016), NO la aplicación (no hay check-then-act).
/// </summary>
public sealed class ReservaRepository(ReservasDbContext db) : IReservaRepository
{
    public async Task ConfirmarAsync(Reserva reserva, CancellationToken ct)
    {
        db.Reservas.Add(reserva);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqlException sql && ClasificacionSqlServer.EsViolacionDeUnico(sql.Number))
        {
            // Perdió la carrera: otro reservó esa noche. 409 sin retry (la tx revierte al hacer dispose).
            throw new HabitacionNoDisponibleException();
        }

        // Un deadlock (1205) NO se captura aquí: se propaga para que PoliticaReintentos lo reejecute.
    }
}
