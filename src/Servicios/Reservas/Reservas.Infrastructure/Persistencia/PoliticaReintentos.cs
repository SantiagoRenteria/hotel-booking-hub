using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Reservas.Infrastructure.Persistencia;

/// <summary>
/// Reintenta una acción SOLO ante deadlock (1205), con backoff + jitter y un tope de intentos.
/// La violación de único (2627/2601) NO se reintenta (es determinística → 409). En 1.6b esta política
/// la usa el <c>TransactionBehavior</c>; aquí queda como componente reutilizable y unit-testeable.
/// </summary>
public static class PoliticaReintentos
{
    public static async Task EjecutarAsync(
        Func<CancellationToken, Task> accion,
        CancellationToken ct,
        int maxIntentos = 3,
        Func<Exception, bool>? esReintentable = null)
    {
        esReintentable ??= EsDeadlock;

        for (var intento = 1; ; intento++)
        {
            try
            {
                await accion(ct);
                return;
            }
            catch (Exception ex) when (intento < maxIntentos && esReintentable(ex))
            {
                var esperaMs = (50 * intento) + Random.Shared.Next(0, 50); // backoff lineal + jitter
                await Task.Delay(TimeSpan.FromMilliseconds(esperaMs), ct);
            }
        }
    }

    /// <summary>True si la excepción (directa o envuelta por EF en <see cref="DbUpdateException"/>) es un deadlock.</summary>
    public static bool EsDeadlock(Exception ex) => ex switch
    {
        SqlException s => ClasificacionSqlServer.EsDeadlock(s.Number),
        DbUpdateException { InnerException: SqlException s } => ClasificacionSqlServer.EsDeadlock(s.Number),
        _ => false,
    };
}
