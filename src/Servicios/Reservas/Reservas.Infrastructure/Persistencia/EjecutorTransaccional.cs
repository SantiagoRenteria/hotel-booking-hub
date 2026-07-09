using System.Data;
using HotelBookingHub.Comun.Excepciones;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Reservas.Domain.Reservas;

namespace Reservas.Infrastructure.Persistencia;

/// <summary>
/// Unidad de trabajo transaccional del BC de Reservas: ejecuta una acción que STAGEA cambios (dominio +
/// outbox) y los confirma en un ÚNICO <c>SaveChanges</c> bajo READ COMMITTED (atomicidad, ADR-016/018).
/// - <c>2627/2601</c> (índice único) → <see cref="HabitacionNoDisponibleException"/>, SIN retry (409 determinístico).
/// - <c>1205</c> (deadlock) → retry acotado (3×, backoff+jitter); antes de cada reintento limpia el
///   <c>ChangeTracker</c> y re-ejecuta la acción, para no re-insertar el estado del intento fallido.
/// </summary>
public sealed class EjecutorTransaccional(ReservasDbContext db)
{
    public async Task EjecutarAsync(Func<CancellationToken, Task> stagearCambios, CancellationToken ct)
    {
        try
        {
            await PoliticaReintentos.EjecutarAsync(
                async token =>
                {
                    // Limpia el estado de un intento previo fallido (1205): la acción vuelve a stagear desde cero.
                    db.ChangeTracker.Clear();

                    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
                    try
                    {
                        await stagearCambios(token);
                        await db.SaveChangesAsync(token);
                        await tx.CommitAsync(token);
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        // Concurrencia optimista (Story 4.2): el UPDATE ... WHERE RowVersion=@original afectó 0 filas
                        // (otra operación resolvió/editó la reserva en medio). Desenlace DETERMINÍSTICO → 409 sin
                        // retry (reintentar sin recargar volvería a fallar). La tx revierte al hacer dispose, así
                        // que la liberación de slots de la perdedora NO se aplica (no hay doble liberación).
                        throw new ConflictoConcurrenciaException(ex);
                    }
                    catch (DbUpdateException ex)
                        when (ex.InnerException is SqlException sql && ClasificacionSqlServer.EsViolacionDeUnico(sql.Number))
                    {
                        // Perdió la carrera por esa noche. 409 sin retry (la tx revierte al hacer dispose).
                        throw new HabitacionNoDisponibleException();
                    }
                },
                ct,
                esReintentable: PoliticaReintentos.EsDeadlock);
        }
        catch (Exception ex) when (PoliticaReintentos.EsDeadlock(ex))
        {
            // Deadlock persistente tras agotar los reintentos (1205): bajo contención equivale a no poder
            // confirmar → se mapea a 409, NUNCA a 500 (determinismo del money test, AC-E1.6c.3). Se usa el
            // MISMO predicado que el retry para cubrir el 1205 tanto crudo (commit/begin) como envuelto por EF.
            throw new HabitacionNoDisponibleException();
        }
    }
}
