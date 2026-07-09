using StackExchange.Redis;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>Opciones del inbox Redis (Story 5.1b).</summary>
/// <param name="Ttl">Vida de la reserva de dedup. DEBE ser mayor que la ventana máxima de re-entrega del broker:
///   si expira antes de que cesen las re-entregas, una re-entrega tardía re-reservaría y duplicaría el efecto.
///   También acota la ventana de pérdida si el proceso cae entre reservar y enviar (tras expirar, la re-entrega
///   re-envía).</param>
public sealed record OpcionesInbox(TimeSpan Ttl)
{
    public OpcionesInbox() : this(TimeSpan.FromHours(24)) { }
}

/// <summary>
/// Implementación de <see cref="IInboxIdempotencia"/> sobre Redis con <c>SET key val NX EX ttl</c> (SETNX+TTL
/// atómico) vía <see cref="IConnectionMultiplexer"/>. Es la dedup best-effort at-least-once del efecto externo
/// (email) — ver la doc del puerto para el patrón y sus límites.
/// </summary>
public sealed class InboxIdempotenciaRedis(IConnectionMultiplexer redis, OpcionesInbox opciones) : IInboxIdempotencia
{
    public Task<bool> IntentarMarcarProcesadoAsync(Guid messageId, int version, string efecto, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // SET key val NX EX ttl (atómico): crea la clave SOLO si no existe y le fija el TTL en la misma operación.
        // Devuelve true si ganó la reserva (primera vez); false si ya estaba (duplicado → descartar el efecto).
        return redis.GetDatabase().StringSetAsync(
            Clave(messageId, version, efecto), "1", opciones.Ttl, when: When.NotExists);
    }

    public Task LiberarAsync(Guid messageId, int version, string efecto, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return redis.GetDatabase().KeyDeleteAsync(Clave(messageId, version, efecto));
    }

    private static string Clave(Guid messageId, int version, string efecto) =>
        $"notif:inbox:{messageId:N}:{version}:{efecto}";
}
