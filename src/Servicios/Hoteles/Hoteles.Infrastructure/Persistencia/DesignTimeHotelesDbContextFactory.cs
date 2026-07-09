using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hoteles.Infrastructure.Persistencia;

/// <summary>Fábrica design-time para `dotnet ef migrations` (no se usa en runtime).</summary>
public sealed class DesignTimeHotelesDbContextFactory : IDesignTimeDbContextFactory<HotelesDbContext>
{
    public HotelesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HotelesDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=Hoteles_DesignTime;Trusted_Connection=True;")
            .Options;
        return new HotelesDbContext(options);
    }
}
