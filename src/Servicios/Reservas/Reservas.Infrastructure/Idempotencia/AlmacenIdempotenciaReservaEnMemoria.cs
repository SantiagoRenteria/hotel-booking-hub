using Microsoft.Extensions.Caching.Memory;
using Reservas.Application.Abstracciones;

namespace Reservas.Infrastructure.Idempotencia;

/// <summary>
/// Fallback en memoria del almacén de idempotencia (dev sin Redis). Respaldado por <see cref="MemoryCache"/> con
/// TTL (evicción automática → sin crecimiento ilimitado). La reserva (SETNX) se serializa con un lock: es correcto
/// para una ÚNICA instancia (dev). NO deduplica entre instancias — para eso está el adaptador Redis.
/// </summary>
public sealed class AlmacenIdempotenciaReservaEnMemoria(OpcionesIdempotenciaReserva opciones) : IAlmacenIdempotenciaReserva, IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly Lock _candado = new();

    public Task<bool> ReservarAsync(string clave, string huellaCuerpo, CancellationToken ct)
    {
        lock (_candado)
        {
            if (_cache.TryGetValue(clave, out _))
            {
                return Task.FromResult(false);
            }

            _cache.Set(clave, new RegistroIdempotencia(huellaCuerpo, null), opciones.Ttl);
            return Task.FromResult(true);
        }
    }

    public Task<RegistroIdempotencia?> ObtenerAsync(string clave, CancellationToken ct) =>
        Task.FromResult(_cache.TryGetValue(clave, out RegistroIdempotencia? registro) ? registro : null);

    public Task FinalizarAsync(string clave, string huellaCuerpo, string respuestaJson, CancellationToken ct)
    {
        lock (_candado)
        {
            _cache.Set(clave, new RegistroIdempotencia(huellaCuerpo, respuestaJson), opciones.Ttl);
        }

        return Task.CompletedTask;
    }

    public Task LiberarAsync(string clave, CancellationToken ct)
    {
        lock (_candado)
        {
            _cache.Remove(clave);
        }

        return Task.CompletedTask;
    }

    public void Dispose() => _cache.Dispose();
}
