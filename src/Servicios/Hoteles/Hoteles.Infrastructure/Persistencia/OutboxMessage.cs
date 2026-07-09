namespace Hoteles.Infrastructure.Persistencia;

/// <summary>
/// Fila del Transactional Outbox de catálogo (Story 2.5). Se escribe en la MISMA transacción que el cambio de
/// dominio: en Hoteles esa transacción es el único <c>SaveChanges</c> del repositorio (Opción U — sin
/// <c>TransactionBehavior</c>), donde la fila de outbox (staged con <c>Add</c>) y la mutación de la habitación
/// entran juntas o ninguna. Clave clustered secuencial <see cref="Seq"/> (orden de polling) + <see cref="MessageId"/>
/// con índice único (dedup del consumidor, E5). El relay reclama por <see cref="ReclamadoEn"/> (lease-expiry por
/// antigüedad → sin huérfanos si un relay muere tras reclamar y antes de publicar). Réplica delgada del outbox de
/// Reservas 1.6b; promoverlo a <c>Comun</c> queda para la regla de tres.
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
