using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Notificaciones.IntegrationTests;

/// <summary>
/// Levanta un Redis real (Testcontainers, imagen pineada) para los tests del inbox de idempotencia del
/// consumidor (Story 5.1b). Expone un <see cref="IConnectionMultiplexer"/> compartido (una conexión, liberada al
/// terminar); el aislamiento entre tests se logra con MessageId únicos por test (no hace falta FLUSHDB).
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _redis =
        new RedisBuilder("redis:7.4-alpine").Build();

    private IConnectionMultiplexer? _multiplexer;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _multiplexer = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_multiplexer is not null)
        {
            await _multiplexer.DisposeAsync();
        }

        await _redis.DisposeAsync();
    }

    /// <summary>El <see cref="IConnectionMultiplexer"/> respaldado por el contenedor Redis (gestionado por la fixture).</summary>
    public IConnectionMultiplexer Redis => _multiplexer!;
}

/// <summary>Colección para compartir el contenedor Redis entre las clases de test del inbox.</summary>
[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>;
