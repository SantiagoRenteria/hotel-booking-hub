using System.Collections.Concurrent;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Implementación en memoria de <see cref="IInboxIdempotencia"/> con semántica <c>SETNX</c> fiel
/// (<see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> es la reserva atómica). Es el <b>fallback</b> para
/// desarrollo local sin Redis (mismo criterio "Redis-si-configurado, si-no memoria" de la caché de 3.2) y sirve
/// de doble determinista en los tests unitarios. NO deduplica entre instancias del worker (para eso está
/// <c>InboxIdempotenciaRedis</c>); en single-instance es correcto.
/// </summary>
public sealed class InboxIdempotenciaEnMemoria : IInboxIdempotencia
{
    private readonly ConcurrentDictionary<string, byte> _reservas = new();

    public Task<bool> IntentarMarcarProcesadoAsync(Guid messageId, int version, string efecto, CancellationToken ct) =>
        // TryAdd es atómico: solo el primero en llegar gana la reserva; el resto ve la clave ya presente.
        Task.FromResult(_reservas.TryAdd(Clave(messageId, version, efecto), 0));

    public Task LiberarAsync(Guid messageId, int version, string efecto, CancellationToken ct)
    {
        _reservas.TryRemove(Clave(messageId, version, efecto), out _);
        return Task.CompletedTask;
    }

    private static string Clave(Guid messageId, int version, string efecto) =>
        $"notif:inbox:{messageId:N}:{version}:{efecto}";
}
