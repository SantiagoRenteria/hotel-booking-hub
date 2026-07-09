using Microsoft.EntityFrameworkCore;
using Reservas.Infrastructure.Mensajeria;
using Reservas.Infrastructure.Persistencia;
using Xunit;

namespace Reservas.UnitTests.Outbox;

/// <summary>
/// Story 4.3 (Task 0) — el outbox soporta MÚLTIPLES eventos por comando con <c>MessageId</c> POR-MENSAJE
/// derivado de forma determinista: ordinal 0 = <c>MessageId</c> del comando (cero cambio para los comandos
/// mono-evento de 1.3/1.6b/2.5/4.1/4.2); ordinal &gt;0 = derivado estable. Estable ante el retry 1205 porque
/// la semilla se fija una vez y <see cref="ContextoMensajeria.ReiniciarConteo"/> corre por-intento → misma
/// secuencia de ordinales → mismos ids → la <c>UNIQUE(MessageId)</c> deduplica.
/// </summary>
public sealed class ColaOutboxGuardTests
{
    private static ReservasDbContext CrearContextoSinConexion() =>
        new(new DbContextOptionsBuilder<ReservasDbContext>().UseSqlServer("Server=none;Database=none;").Options);

    private static IReadOnlyList<Guid> MessageIdsStageados(ReservasDbContext db) =>
        [.. db.ChangeTracker.Entries<OutboxMessage>().Select(e => e.Entity.MessageId)];

    [Fact]
    public void Primer_evento_usa_el_MessageId_del_comando()
    {
        using var db = CrearContextoSinConexion();
        var idComando = Guid.CreateVersion7();
        var cola = new ColaOutbox(db, new ContextoMensajeria { MessageId = idComando });

        cola.Encolar("EventoA.v1", 1, Guid.NewGuid(), new { }, traceId: null);

        Assert.Equal(idComando, Assert.Single(MessageIdsStageados(db)));
    }

    [Fact]
    public void Dos_eventos_en_el_mismo_comando_stagean_dos_filas_con_MessageId_distinto()
    {
        using var db = CrearContextoSinConexion();
        var cola = new ColaOutbox(db, new ContextoMensajeria { MessageId = Guid.CreateVersion7() });

        cola.Encolar("EventoA.v1", 1, Guid.NewGuid(), new { }, traceId: null);
        cola.Encolar("EventoB.v1", 1, Guid.NewGuid(), new { }, traceId: null);

        var ids = MessageIdsStageados(db);
        Assert.Equal(2, ids.Count);
        Assert.Equal(2, ids.Distinct().Count()); // sin colisión → la UNIQUE(MessageId) no se viola
    }

    [Fact]
    public void Los_MessageId_derivados_son_estables_entre_reintentos()
    {
        var idComando = Guid.CreateVersion7();

        Guid[] Corrida()
        {
            using var db = CrearContextoSinConexion();
            var contexto = new ContextoMensajeria { MessageId = idComando };
            var cola = new ColaOutbox(db, contexto);
            cola.Encolar("EventoA.v1", 1, Guid.NewGuid(), new { }, traceId: null);
            cola.Encolar("EventoB.v1", 1, Guid.NewGuid(), new { }, traceId: null);
            return [.. MessageIdsStageados(db)];
        }

        // El retry 1205 re-ejecuta el handler con la MISMA semilla y el ordinal reiniciado → mismos ids.
        Assert.Equal(Corrida(), Corrida());
    }
}
