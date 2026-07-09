using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Atomicidad ante fallo (AC-E1.6b.3), en colección aislada con paralelización deshabilitada y su propio
/// contenedor: un fallo inyectado al escribir el outbox NO debe dejar ni la reserva ni la fila de outbox.
/// </summary>
[Collection("OutboxFaultInjection")]
public sealed class OutboxFaultInjectionTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Fallo_inyectado_entre_reserva_y_outbox_no_deja_ninguna_de_las_dos()
    {
        var reserva = HelpersOutbox.NuevaReserva();

        await using (var db = fixture.CrearContexto(new InterceptorFallaOutbox()))
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                HelpersOutbox.ConfirmarConOutboxAsync(db, reserva, Guid.CreateVersion7(), CancellationToken.None));
        }

        await using var verify = fixture.CrearContexto();
        Assert.Equal(0, await verify.Reservas.CountAsync(r => r.Id == reserva.Id));
        Assert.Equal(0, await verify.OutboxMessages.CountAsync(o => o.AggregateId == reserva.Id));
    }
}

/// <summary>Colección aislada (propio contenedor, sin paralelización) para el fault-injection del outbox.</summary>
[CollectionDefinition("OutboxFaultInjection", DisableParallelization = true)]
public sealed class OutboxFaultInjectionCollection : ICollectionFixture<SqlServerFixture>;
