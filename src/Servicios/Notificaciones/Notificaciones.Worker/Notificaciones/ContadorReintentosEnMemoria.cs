using System.Collections.Concurrent;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Contador de intentos en memoria (fallback local sin Redis + doble de test). El incremento es atómico
/// (<see cref="ConcurrentDictionary{TKey,TValue}.AddOrUpdate(TKey,TValue,System.Func{TKey,TValue,TValue})"/>).
/// No comparte estado entre instancias del worker (para eso está la variante Redis <c>INCR</c>).
/// </summary>
public sealed class ContadorReintentosEnMemoria : IContadorReintentos
{
    private readonly ConcurrentDictionary<Guid, long> _intentos = new();

    public Task<long> IncrementarAsync(Guid messageId, CancellationToken ct) =>
        Task.FromResult(_intentos.AddOrUpdate(messageId, 1, (_, previo) => previo + 1));

    public Task ReiniciarAsync(Guid messageId, CancellationToken ct)
    {
        _intentos.TryRemove(messageId, out _);
        return Task.CompletedTask;
    }
}
