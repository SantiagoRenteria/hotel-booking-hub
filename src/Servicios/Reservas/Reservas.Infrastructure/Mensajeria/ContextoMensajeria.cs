namespace Reservas.Infrastructure.Mensajeria;

/// <summary>
/// Portador scoped del <c>MessageId</c> del comando en curso. El <c>TransactionBehavior</c> lo asigna UNA
/// sola vez al entrar (antes del retry 1205); la cola de outbox lo lee al encolar. Si se regenerara en el
/// retry, el <c>UNIQUE(OutboxMessages.MessageId)</c> no deduplicaría (anti-patrón crítico, ADR-018).
/// </summary>
public sealed class ContextoMensajeria
{
    public Guid MessageId { get; set; }
}
