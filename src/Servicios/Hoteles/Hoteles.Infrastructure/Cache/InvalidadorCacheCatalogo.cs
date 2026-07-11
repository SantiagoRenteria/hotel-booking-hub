using Hoteles.Application.Abstracciones;
using StackExchange.Redis;

namespace Hoteles.Infrastructure.Cache;

/// <summary>
/// Invalidador de caché sobre Redis (Story T.6): <c>INCR</c> atómico de la clave-generación → las páginas
/// cacheadas de la generación anterior quedan inalcanzables al instante (la próxima lectura arma una clave nueva
/// → miss → SQL fresco). No borra por patrón (Redis no lo hace barato); las claves viejas expiran por TTL.
/// </summary>
public sealed class InvalidadorCacheCatalogoRedis(IConnectionMultiplexer redis) : IInvalidadorCacheCatalogo
{
    public Task InvalidarHotelesDeAgenteAsync(string agente, CancellationToken ct) =>
        redis.GetDatabase().StringIncrementAsync(ClavesCacheCatalogo.GenHoteles(agente));

    public Task InvalidarHabitacionesDeHotelAsync(Guid hotelId, CancellationToken ct) =>
        redis.GetDatabase().StringIncrementAsync(ClavesCacheCatalogo.GenHabitaciones(hotelId));
}

/// <summary>No-op cuando no hay Redis configurado (tests/local sin caché): la lectura ya va directo a SQL.</summary>
public sealed class InvalidadorCacheCatalogoNoop : IInvalidadorCacheCatalogo
{
    public Task InvalidarHotelesDeAgenteAsync(string agente, CancellationToken ct) => Task.CompletedTask;

    public Task InvalidarHabitacionesDeHotelAsync(Guid hotelId, CancellationToken ct) => Task.CompletedTask;
}
