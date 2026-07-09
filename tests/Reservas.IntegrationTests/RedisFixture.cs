using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using Testcontainers.Redis;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Levanta un Redis real (Testcontainers, imagen pineada) para los tests de caché de lectura (Story 3.2).
/// Cada test crea su propio <see cref="IDistributedCache"/> apuntado al contenedor; el aislamiento entre tests
/// se logra con claves de ciudad únicas (no hace falta FLUSHDB entre casos).
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _redis =
        new RedisBuilder("redis:7.4-alpine").Build();

    public Task InitializeAsync() => _redis.StartAsync();

    public Task DisposeAsync() => _redis.DisposeAsync().AsTask();

    /// <summary>Crea un <see cref="IDistributedCache"/> respaldado por el contenedor Redis.</summary>
    public IDistributedCache CrearCache() =>
        new RedisCache(Options.Create(new RedisCacheOptions { Configuration = _redis.GetConnectionString() }));
}
