using Microsoft.EntityFrameworkCore;
using Reservas.Domain.Reservas;
using Reservas.Infrastructure.Idempotencia;
using Reservas.Infrastructure.Proyeccion;

namespace Reservas.Infrastructure.Persistencia;

/// <summary>
/// DbContext del BC de Reservas (una BD por servicio). Configura las claves según ADR-017
/// (UUID v7 no-clustered + <c>Seq</c> secuencial clustered como shadow property) y el árbitro
/// anti-overbooking: la PK clustered compuesta <c>(HabitacionId, Noche)</c> de <c>NochesHabitacion</c>.
/// </summary>
public sealed class ReservasDbContext(DbContextOptions<ReservasDbContext> options) : DbContext(options)
{
    public DbSet<Reserva> Reservas => Set<Reserva>();
    public DbSet<NocheHabitacion> NochesHabitacion => Set<NocheHabitacion>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // Read-model de catálogo (E3) alimentado por eventos de Hoteles + inbox de idempotencia del consumidor.
    public DbSet<ProyeccionHabitacion> ProyeccionesHabitacion => Set<ProyeccionHabitacion>();
    public DbSet<ProyeccionHotelEstado> ProyeccionesHotelEstado => Set<ProyeccionHotelEstado>();
    public DbSet<MensajeProcesado> MensajesProcesados => Set<MensajeProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Reserva>(b =>
        {
            b.ToTable("Reservas");

            // Identidad UUID v7 como PK NO-clustered; clustering key = Seq (bigint IDENTITY) shadow.
            b.HasKey(r => r.Id).IsClustered(false);
            b.Property<long>("Seq").ValueGeneratedOnAdd().UseIdentityColumn();
            b.HasIndex("Seq").IsUnique().IsClustered();

            // Concurrencia optimista.
            b.Property<byte[]>("RowVersion").IsRowVersion();

            b.Property(r => r.HabitacionId);
            b.Property(r => r.Estado).HasConversion<string>().HasMaxLength(32);

            // Estancia como owned type (columnas Entrada/Salida); Noches es computado → ignorar.
            b.OwnsOne(r => r.Estancia, e =>
            {
                e.Property(x => x.Entrada).HasColumnName("Entrada");
                e.Property(x => x.Salida).HasColumnName("Salida");
                e.Ignore(x => x.Noches);
            });

            // Slots como parte del aggregate (acceso por campo _noches).
            b.HasMany(r => r.Noches)
                .WithOne()
                .HasForeignKey(n => n.ReservaId)
                .OnDelete(DeleteBehavior.Cascade);
            b.Navigation(r => r.Noches).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<NocheHabitacion>(b =>
        {
            b.ToTable("NochesHabitacion");
            // El ÁRBITRO anti-overbooking: PK clustered compuesta. La unicidad impide doble reserva de la noche.
            b.HasKey(n => new { n.HabitacionId, n.Noche });
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessages");
            b.HasKey(o => o.Seq);
            b.Property(o => o.Seq).UseIdentityColumn();
            b.HasIndex(o => o.MessageId).IsUnique();
            b.Property(o => o.Type).HasMaxLength(200);
            b.Property(o => o.Estado).HasMaxLength(32);
            // Índice de polling del relay: pendientes en orden de secuencia.
            b.HasIndex(o => new { o.Estado, o.Seq });
        });

        modelBuilder.Entity<ProyeccionHabitacion>(b =>
        {
            b.ToTable("ProyeccionHabitacion");
            b.HasKey(p => p.HabitacionId);
            b.Property(p => p.Ciudad).HasMaxLength(120);
            b.Property(p => p.Tipo).HasMaxLength(100);
            b.Property(p => p.Ubicacion).HasMaxLength(200);
            b.Property(p => p.Estado).HasMaxLength(20);
            b.Property(p => p.CostoBase).HasPrecision(18, 2);
            b.Property(p => p.Impuestos).HasPrecision(18, 2);
            b.Ignore(p => p.Hidratada); // derivado en C# (VersionEstatico != null); 3.2 filtra por VersionEstatico.
            // Índice para la búsqueda por ciudad (3.2), acotando a filas ya hidratadas.
            b.HasIndex(p => p.Ciudad);
        });

        modelBuilder.Entity<ProyeccionHotelEstado>(b =>
        {
            b.ToTable("ProyeccionHotelEstado");
            b.HasKey(p => p.HotelId);
            // Dimensión independiente del estado de hotel (3.2): la búsqueda excluye habitaciones cuyo hotel esté
            // inactivo. Fila ausente = hotel activo (COALESCE en la query), coherente con E2 sin eventos de hotel.
        });

        modelBuilder.Entity<MensajeProcesado>(b =>
        {
            b.ToTable("MensajesProcesados");
            b.HasKey(m => m.MessageId); // dedup del inbox (AC-E3.1.4)
        });
    }
}
