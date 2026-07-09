using Hoteles.Domain.Habitaciones;
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
    public DbSet<Habitacion> Habitaciones => Set<Habitacion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Hotel>(b =>
        {
            b.ToTable("Hoteles");

            b.HasKey(h => h.Id).IsClustered(false);
            b.Property<long>("Seq").ValueGeneratedOnAdd().UseIdentityColumn();
            b.HasIndex("Seq").IsUnique().IsClustered();
            b.Property<byte[]>("RowVersion").IsRowVersion();

            // Soft delete (2.2): baja lógica + query filter global → un hotel eliminado deja de aparecer en
            // consultas y de ofertar. Marcar Eliminado=true es un UPDATE, así que el rowversion cambia y una
            // edición concurrente que pierda contra el borrado obtiene 409 (no un last-write-wins silencioso).
            b.HasQueryFilter(h => !h.Eliminado);

            b.Property(h => h.Nombre).HasMaxLength(LongitudesHotel.Nombre);
            b.Property(h => h.Ciudad).HasMaxLength(LongitudesHotel.Ciudad);
            b.Property(h => h.Direccion).HasMaxLength(LongitudesHotel.Direccion);
            b.Property(h => h.Descripcion).HasMaxLength(LongitudesHotel.Descripcion);
            b.Property(h => h.Estado).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Habitacion>(b =>
        {
            b.ToTable("Habitaciones");

            b.HasKey(h => h.Id).IsClustered(false);
            b.Property<long>("Seq").ValueGeneratedOnAdd().UseIdentityColumn();
            b.HasIndex("Seq").IsUnique().IsClustered();
            b.Property<byte[]>("RowVersion").IsRowVersion();

            // Aggregate independiente del hotel: se referencia por HotelId (solo índice, SIN FK constraint) para
            // no romper su gestión/versionado separados. Índice por HotelId para listar/filtrar por hotel (E3).
            b.HasIndex(h => h.HotelId);

            b.Property(h => h.Tipo).HasMaxLength(LongitudesHabitacion.Tipo);
            b.Property(h => h.Ubicacion).HasMaxLength(LongitudesHabitacion.Ubicacion);
            b.Property(h => h.CostoBase).HasPrecision(LongitudesHabitacion.PrecisionMonto, LongitudesHabitacion.EscalaMonto);
            b.Property(h => h.Impuestos).HasPrecision(LongitudesHabitacion.PrecisionMonto, LongitudesHabitacion.EscalaMonto);
            b.Property(h => h.Estado).HasConversion<string>().HasMaxLength(20);
        });
    }
}
