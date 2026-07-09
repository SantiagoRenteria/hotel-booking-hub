using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Reservas.Infrastructure.Persistencia;
using Testcontainers.MsSql;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Levanta un SQL Server real (Testcontainers, imagen pineada) y aplica la migración una vez.
/// Cada test crea su propio <see cref="ReservasDbContext"/> (los DbContext no son thread-safe) y usa
/// `HabitacionId` únicos, de modo que los tests no colisionan sin necesidad de reset entre cada uno.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        await using var db = CrearContexto();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _sql.DisposeAsync().AsTask();

    /// <summary>Cadena de conexión al contenedor (para armar contenedores DI completos en los tests).</summary>
    public string CadenaConexion => _sql.GetConnectionString();

    public ReservasDbContext CrearContexto(params IInterceptor[] interceptores)
    {
        var builder = new DbContextOptionsBuilder<ReservasDbContext>()
            .UseSqlServer(_sql.GetConnectionString());

        if (interceptores.Length > 0)
        {
            builder.AddInterceptors(interceptores);
        }

        return new ReservasDbContext(builder.Options);
    }
}

[CollectionDefinition("sqlserver")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>, ICollectionFixture<RedisFixture>;
