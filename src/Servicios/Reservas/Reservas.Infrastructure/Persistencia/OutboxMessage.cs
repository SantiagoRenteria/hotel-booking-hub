namespace Reservas.Infrastructure.Persistencia;

/// <summary>
/// Fila del Transactional Outbox. Se escribe en la MISMA transacción que el cambio de dominio (atomicidad,
/// 1.6b). Clave clustered secuencial <see cref="Seq"/> (orden de polling) + <see cref="MessageId"/> con
/// índice único (dedup — el consumidor deduplica por él, E5). El relay reclama por <see cref="ReclamadoEn"/>
/// (lease-expiry por antigüedad → sin mensajes huérfanos si un relay muere tras reclamar y antes de enviar).
/// </summary>
public sealed class OutboxMessage
{
    public const string Pendiente = "Pendiente";
    public const string Enviada = "Enviada";

    public long Seq { get; private set; }
    public Guid MessageId { get; private set; }
    public Guid AggregateId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public string Estado { get; private set; } = Pendiente;
    public int Intentos { get; private set; }

    /// <summary>Instante del último reclamo por el relay; null si nunca se reclamó (lease-expiry por antigüedad).</summary>
    public DateTimeOffset? ReclamadoEn { get; private set; }

    private OutboxMessage() { } // EF Core

    public static OutboxMessage Crear(Guid messageId, Guid aggregateId, string type, string payload, DateTimeOffset occurredAt) =>
        new()
        {
            MessageId = messageId,
            AggregateId = aggregateId,
            Type = type,
            Payload = payload,
            OccurredAt = occurredAt,
            Estado = Pendiente,
            Intentos = 0,
        };

    /// <summary>El relay toma la fila para intentar publicarla: cuenta el intento y marca el lease.</summary>
    public void Reclamar(DateTimeOffset ahora)
    {
        Intentos++;
        ReclamadoEn = ahora;
    }

    /// <summary>Se llama SOLO tras publicar con éxito (sin mark-sent prematuro → no se pierde el evento).</summary>
    public void MarcarEnviada() => Estado = Enviada;
}
