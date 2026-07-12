using Hoteles.Application.Abstracciones;
using Hoteles.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Testcontainers.MsSql;
using Testcontainers.Redis;
using Xunit;

namespace Hoteles.IntegrationTests;

/// <summary>
/// Story T.6 — levanta SQL Server + Redis reales (Testcontainers) para probar la caché del catálogo. El caché
/// solo se prueba de verdad end-to-end (decorador + Redis + invalidador comparten la MISMA conexión): un fake
/// en memoria no detectaría un desajuste de claves entre el lector cacheado y el invalidador por generación.
/// Cada test usa agentes/hoteles únicos → sin FLUSHDB entre tests.
/// </summary>
public sealed class CacheCatalogoFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();

    private readonly RedisContainer _redis =
        new RedisBuilder("redis:7.4-alpine").Build();

    private IConnectionMultiplexer? _multiplexer;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _redis.StartAsync());
        await using var db = CrearContexto();
        await db.Database.MigrateAsync();
        _multiplexer = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_multiplexer is not null)
        {
            await _multiplexer.DisposeAsync();
        }

        await Task.WhenAll(_sql.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }

    /// <summary>Conexión Redis compartida (la MISMA que usan el decorador y el invalidador en cada test).</summary>
    public IConnectionMultiplexer Redis => _multiplexer!;

    /// <summary>Contexto sin identidad (query filter inactivo) — para sembrar datos de cualquier agente.</summary>
    public HotelesDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<HotelesDbContext>().UseSqlServer(_sql.GetConnectionString()).Options);

    /// <summary>Contexto con identidad de agente (activa el aislamiento por propietario, Story 6.3).</summary>
    public HotelesDbContext CrearContextoConAgente(string? agente) =>
        new(new DbContextOptionsBuilder<HotelesDbContext>().UseSqlServer(_sql.GetConnectionString()).Options,
            new ContextoAgenteFijo(agente));

    public sealed class ContextoAgenteFijo(string? agente) : IContextoAgente
    {
        public string? AgenteActual => agente;
    }
}

[CollectionDefinition("cache-catalogo")]
public sealed class CacheCatalogoCollection : ICollectionFixture<CacheCatalogoFixture>;
