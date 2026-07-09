using Microsoft.EntityFrameworkCore;
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

    public ReservasDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<ReservasDbContext>()
            .UseSqlServer(_sql.GetConnectionString())
            .Options;
        return new ReservasDbContext(options);
    }
}

[CollectionDefinition("sqlserver")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
