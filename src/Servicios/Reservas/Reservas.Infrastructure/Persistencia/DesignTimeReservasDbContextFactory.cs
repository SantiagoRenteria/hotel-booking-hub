using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Reservas.Infrastructure.Persistencia;

/// <summary>
/// Fábrica en tiempo de diseño para las migraciones EF Core (`dotnet ef migrations`). La cadena de
/// conexión es un placeholder: las migraciones se generan a partir del modelo, no conectan a la BD.
/// </summary>
public sealed class DesignTimeReservasDbContextFactory : IDesignTimeDbContextFactory<ReservasDbContext>
{
    public ReservasDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ReservasDbContext>()
            .UseSqlServer("Server=localhost;Database=db_reservas;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new ReservasDbContext(options);
    }
}
