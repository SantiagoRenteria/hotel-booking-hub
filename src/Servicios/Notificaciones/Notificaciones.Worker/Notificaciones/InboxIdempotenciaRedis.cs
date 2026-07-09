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
        _ = (redis, opciones); // TODO 5.1b GREEN: SET key val NX EX ttl.
        throw new NotImplementedException("5.1b GREEN pendiente: SETNX+TTL sobre Redis.");
    }

    public Task LiberarAsync(Guid messageId, int version, string efecto, CancellationToken ct)
    {
        _ = redis; // TODO 5.1b GREEN: DEL de la reserva.
        throw new NotImplementedException("5.1b GREEN pendiente: DEL de la reserva.");
    }
}
