using Hoteles.Application.Abstracciones;
using Hoteles.Domain.Habitaciones;
using Hoteles.Domain.Hoteles;
using Microsoft.EntityFrameworkCore;

namespace Hoteles.Infrastructure.Persistencia;

/// <summary>
/// DbContext del BC de Hoteles (BD por servicio, independiente de Reservas — los BC solo hablan por eventos).
/// Claves ADR-017: PK UUID v7 no-clustered + <c>Seq bigint IDENTITY</c> shadow clustered + <c>rowversion</c>
/// (habilita la concurrencia optimista de 2.2). El outbox de catálogo se añade en 2.5.
/// <para><b>Aislamiento entre agentes (Story 6.3):</b> un query filter global por <c>AgentePropietario</c> hace
/// invisible el hotel de otro agente → toda carga-para-mutar devuelve 404 sin guard por handler ("un solo lugar
/// decide"). La identidad viene del <see cref="IContextoAgente"/> (claim del token). En contextos SIN identidad
/// (migraciones/design-time/siembra de tests) el filtro por propietario NO aplica; en el flujo HTTP real el
/// agente SIEMPRE está presente (lo garantiza el RBAC de 6.2, que exige rol Agente con su claim).</para>
/// </summary>
public sealed class HotelesDbContext(DbContextOptions<HotelesDbContext> options, IContextoAgente? contextoAgente = null) : DbContext(options)
{
    private readonly string? _agenteActual = contextoAgente?.AgenteActual;

    public DbSet<Hotel> Hoteles => Set<Hotel>();
    public DbSet<Habitacion> Habitaciones => Set<Habitacion>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Hotel>(b =>
        {
            b.ToTable("Hoteles");

            b.HasKey(h => h.Id).IsClustered(false);
            b.Property<long>("Seq").ValueGeneratedOnAdd().UseIdentityColumn();
            b.HasIndex("Seq").IsUnique().IsClustered();
            b.Property<byte[]>("RowVersion").IsRowVersion();

            // Soft delete (2.2) + aislamiento por propietario (6.3) en el MISMO query filter global. El hotel
            // eliminado deja de aparecer; el de otro agente es invisible (carga-para-mutar → 404). El guard
            // `_agenteActual == null` desactiva SOLO el filtro por propietario en contextos sin identidad
            // (migraciones/design-time/siembra); en el flujo HTTP real el RBAC (6.2) garantiza que hay agente.
            b.HasQueryFilter(h => !h.Eliminado && (_agenteActual == null || h.AgentePropietario == _agenteActual));

            b.Property(h => h.AgentePropietario).IsRequired().HasMaxLength(LongitudesHotel.AgentePropietario);
            b.Property(h => h.Nombre).HasMaxLength(LongitudesHotel.Nombre);
            b.Property(h => h.Ciudad).HasMaxLength(LongitudesHotel.Ciudad);
            b.Property(h => h.Direccion).HasMaxLength(LongitudesHotel.Direccion);
            b.Property(h => h.Descripcion).HasMaxLength(LongitudesHotel.Descripcion);
            b.Property(h => h.Estado).HasConversion<string>().HasMaxLength(20);

            // Version de secuencia del estado (3.2): order key monotónico de HotelHabilitado/HotelDeshabilitado.
            // Columna de negocio propia, INDEPENDIENTE del rowversion de concurrencia.
            b.Property(h => h.Version);
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

            // Version de secuencia del agregado (2.5): parte monotónica del order key de los eventos de catálogo.
            // Columna de negocio propia, INDEPENDIENTE del rowversion de concurrencia (miden cosas distintas).
            b.Property(h => h.Version);
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessages");
            b.HasKey(o => o.Seq);
            b.Property(o => o.Seq).UseIdentityColumn();
            b.HasIndex(o => o.MessageId).IsUnique(); // dedup del consumidor (E5)
            b.Property(o => o.Type).HasMaxLength(200);
            b.Property(o => o.Estado).HasMaxLength(32);
            // Índice de polling del relay: pendientes en orden de secuencia.
            b.HasIndex(o => new { o.Estado, o.Seq });
        });
    }
}
