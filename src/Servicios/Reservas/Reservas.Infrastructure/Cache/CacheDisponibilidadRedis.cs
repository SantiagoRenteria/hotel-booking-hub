using Microsoft.Extensions.Caching.Distributed;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.BuscarDisponibilidad;

namespace Reservas.Infrastructure.Cache;

/// <summary>
/// Caché de disponibilidad sobre Redis vía <see cref="IDistributedCache"/> (AC-E3.2.3). STUB de la fase RED —
/// el esquema de claves generacional por ciudad y la (de)serialización llegan en GREEN.
/// </summary>
public sealed class CacheDisponibilidadRedis(IDistributedCache cache, OpcionesCacheDisponibilidad opciones)
    : ICacheDisponibilidad, IInvalidadorCacheDisponibilidad
{
    public Task<IReadOnlyList<HabitacionDisponibleDto>?> ObtenerAsync(BuscarDisponibilidadQuery consulta, CancellationToken ct) =>
        throw new NotImplementedException(
            $"RED: lectura de caché pendiente (GREEN). cache={cache.GetType().Name}, ciudad={consulta.Ciudad}");

    public Task GuardarAsync(BuscarDisponibilidadQuery consulta, IReadOnlyList<HabitacionDisponibleDto> resultado, CancellationToken ct) =>
        throw new NotImplementedException(
            $"RED: escritura de caché pendiente (GREEN). ttl={opciones.Ttl}, n={resultado.Count}, ciudad={consulta.Ciudad}");

    public Task InvalidarCiudadAsync(string ciudad, CancellationToken ct) =>
        throw new NotImplementedException(
            $"RED: invalidación por ciudad pendiente (GREEN). cache={cache.GetType().Name}, ciudad={ciudad}");
}
