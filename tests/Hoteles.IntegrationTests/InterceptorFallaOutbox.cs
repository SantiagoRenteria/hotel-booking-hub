using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hoteles.IntegrationTests;

/// <summary>
/// Harness de fault-injection (Story 2.5, SOLO tests): lanza al ejecutar el comando que inserta en
/// <c>OutboxMessages</c>, dentro de la misma transacción del cambio de la habitación. Provoca el fallo de forma
/// determinista para demostrar la atomicidad (ni la habitación ni la fila de outbox deben quedar). Solo
/// intercepta lecturas (los INSERT con OUTPUT son readers); el <c>DELETE</c> de limpieza es NonQuery y no se toca.
/// </summary>
public sealed class InterceptorFallaOutbox : DbCommandInterceptor
{
    public sealed class FallaInyectada() : Exception("Falla inyectada al escribir el outbox (test).");

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        LanzarSiTocaOutbox(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        LanzarSiTocaOutbox(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private static void LanzarSiTocaOutbox(DbCommand command)
    {
        if (command.CommandText.Contains("OutboxMessages", StringComparison.Ordinal))
        {
            throw new FallaInyectada();
        }
    }
}
