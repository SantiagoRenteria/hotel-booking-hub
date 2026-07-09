namespace Reservas.Infrastructure.Mensajeria;

/// <summary>
/// Portador scoped del <c>MessageId</c> del comando en curso. El <c>TransactionBehavior</c> lo asigna UNA
/// sola vez al entrar (antes del retry 1205); la cola de outbox lo lee al encolar. Si se regenerara en el
/// retry, el <c>UNIQUE(OutboxMessages.MessageId)</c> no deduplicaría (anti-patrón crítico, ADR-018).
/// </summary>
public sealed class ContextoMensajeria
{
    private int _eventosEncolados;

    public Guid MessageId { get; set; }

    /// <summary>
    /// Reinicia el conteo de eventos encolados. Lo llama el <c>TransactionBehavior</c> al inicio de CADA
    /// intento (junto al <c>ChangeTracker.Clear()</c> del retry 1205), de modo que un reintento legítimo
    /// no dispare el guard de "1 evento por comando".
    /// </summary>
    public void ReiniciarConteo() => _eventosEncolados = 0;

    /// <summary>Registra un evento encolado y devuelve el total acumulado en el comando/intento actual.</summary>
    public int RegistrarEventoEncolado() => ++_eventosEncolados;
}
