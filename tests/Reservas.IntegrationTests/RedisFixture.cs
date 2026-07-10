using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using Testcontainers.Redis;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Levanta un Redis real (Testcontainers, imagen pineada) para los tests de caché de lectura (Story 3.2).
/// Los tests comparten un único <see cref="IDistributedCache"/> (una conexión), que la fixture libera al
/// terminar; el aislamiento entre tests se logra con claves de ciudad únicas (no hace falta FLUSHDB).
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _redis =
        new RedisBuilder("redis:7.4-alpine").Build();

    private RedisCache? _cache;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _cache = new RedisCache(Options.Create(new RedisCacheOptions { Configuration = _redis.GetConnectionString() }));
    }

    public async Task DisposeAsync()
    {
        _cache?.Dispose();
        await _redis.DisposeAsync();
    }

    /// <summary>El <see cref="IDistributedCache"/> respaldado por el contenedor Redis (gestionado por la fixture).</summary>
    public IDistributedCache Cache => _cache!;
}
