using Hoteles.Domain.Hoteles;
using Microsoft.EntityFrameworkCore;

namespace Hoteles.Infrastructure.Persistencia;

/// <summary>
/// DbContext del BC de Hoteles (BD por servicio, independiente de Reservas — los BC solo hablan por eventos).
/// Claves ADR-017: PK UUID v7 no-clustered + <c>Seq bigint IDENTITY</c> shadow clustered + <c>rowversion</c>
/// (habilita la concurrencia optimista de 2.2). El outbox de catálogo se añade en 2.5.
/// </summary>
public sealed class HotelesDbContext(DbContextOptions<HotelesDbContext> options) : DbContext(options)
{
    public DbSet<Hotel> Hoteles => Set<Hotel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Hotel>(b =>
        {
            b.ToTable("Hoteles");

            b.HasKey(h => h.Id).IsClustered(false);
            b.Property<long>("Seq").ValueGeneratedOnAdd().UseIdentityColumn();
            b.HasIndex("Seq").IsUnique().IsClustered();
            b.Property<byte[]>("RowVersion").IsRowVersion();

            b.Property(h => h.Nombre).HasMaxLength(200);
            b.Property(h => h.Ciudad).HasMaxLength(120);
            b.Property(h => h.Direccion).HasMaxLength(300);
            b.Property(h => h.Descripcion).HasMaxLength(2000);
            b.Property(h => h.Estado).HasConversion<string>().HasMaxLength(20);
        });
    }
}
