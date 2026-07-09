using Microsoft.EntityFrameworkCore;
using Reservas.Infrastructure.Mensajeria;
using Reservas.Infrastructure.Persistencia;

namespace Reservas.UnitTests.Outbox;

/// <summary>
/// Guard del contrato "1 evento por comando" (decisión de code-review 1.6b, party-mode): un segundo
/// evento en el mismo comando lanza <see cref="InvalidOperationException"/> (NO una excepción de negocio,
/// así que no se traduce a 409) en vez de colisionar silenciosamente en <c>UNIQUE(MessageId)</c>.
/// Contexto sin conexión: <c>Encolar</c> solo stagea en el ChangeTracker; el guard corta antes de tocar la BD.
/// </summary>
public sealed class ColaOutboxGuardTests
{
    private static ReservasDbContext CrearContextoSinConexion() =>
        new(new DbContextOptionsBuilder<ReservasDbContext>().UseSqlServer("Server=none;Database=none;").Options);

    [Fact]
    public void Encolar_un_segundo_evento_en_el_mismo_comando_lanza_invalid_operation()
    {
        using var db = CrearContextoSinConexion();
        var contexto = new ContextoMensajeria { MessageId = Guid.CreateVersion7() };
        var cola = new ColaOutbox(db, contexto);

        cola.Encolar("EventoA.v1", 1, Guid.NewGuid(), new { }, traceId: null); // 1º: OK

        Assert.Throws<InvalidOperationException>(() =>
            cola.Encolar("EventoB.v1", 1, Guid.NewGuid(), new { }, traceId: null)); // 2º: guard
    }

    [Fact]
    public void Reiniciar_conteo_permite_encolar_de_nuevo_en_el_reintento()
    {
        using var db = CrearContextoSinConexion();
        var contexto = new ContextoMensajeria { MessageId = Guid.CreateVersion7() };
        var cola = new ColaOutbox(db, contexto);

        cola.Encolar("EventoA.v1", 1, Guid.NewGuid(), new { }, traceId: null);

        // Simula el reset por-intento del TransactionBehavior + ChangeTracker.Clear del retry 1205.
        contexto.ReiniciarConteo();
        db.ChangeTracker.Clear();

        var ex = Record.Exception(() => cola.Encolar("EventoA.v1", 1, Guid.NewGuid(), new { }, traceId: null));
        Assert.Null(ex); // un solo evento del reintento NO dispara el guard
    }
}
