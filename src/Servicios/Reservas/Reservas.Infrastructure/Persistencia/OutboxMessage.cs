namespace Reservas.Infrastructure.Persistencia;

/// <summary>
/// Fila del Transactional Outbox. En la Story 1.5 solo se define la TABLA (la escritura atómica con
/// la reserva y el relay llegan en 1.6b). Clave clustered secuencial <see cref="Seq"/> (orden de
/// polling) + <see cref="MessageId"/> con índice único (dedup — el consumidor deduplica por él).
/// </summary>
public sealed class OutboxMessage
{
    public long Seq { get; private set; }
    public Guid MessageId { get; private set; }
    public Guid AggregateId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public string Estado { get; private set; } = "Pendiente";
    public int Intentos { get; private set; }

    private OutboxMessage() { } // EF Core
}
