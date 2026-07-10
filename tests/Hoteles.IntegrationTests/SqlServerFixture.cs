using Hoteles.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.MsSql;
using Xunit;

namespace Hoteles.IntegrationTests;

/// <summary>
/// Levanta un SQL Server real (Testcontainers, imagen pineada) y aplica la migración una vez. La concurrencia
/// optimista por <c>rowversion</c> SOLO se prueba de verdad contra el motor: un mock de
/// <c>DbUpdateConcurrencyException</c> no detectaría un token mal mapeado (last-write-wins silencioso). Cada
/// test crea su propio hotel (Id único) y su propio <see cref="HotelesDbContext"/> (no son thread-safe).
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

    public HotelesDbContext CrearContexto(params IInterceptor[] interceptores)
    {
        var builder = new DbContextOptionsBuilder<HotelesDbContext>()
            .UseSqlServer(_sql.GetConnectionString());

        if (interceptores.Length > 0)
        {
            builder.AddInterceptors(interceptores);
        }

        return new HotelesDbContext(builder.Options);
    }

    /// <summary>
    /// Contexto CON identidad de agente (Story 6.3): activa el query filter por propietario, de modo que un
    /// hotel de otro agente queda invisible. Sin argumento equivale al contexto sin identidad (filtro inactivo).
    /// </summary>
    public HotelesDbContext CrearContextoConAgente(string? agente)
    {
        var builder = new DbContextOptionsBuilder<HotelesDbContext>()
            .UseSqlServer(_sql.GetConnectionString());

        return new HotelesDbContext(builder.Options, new ContextoAgenteFijo(agente));
    }

    private sealed class ContextoAgenteFijo(string? agente) : Hoteles.Application.Abstracciones.IContextoAgente
    {
        public string? AgenteActual => agente;
    }
}

[CollectionDefinition("sqlserver-hoteles")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
