using Microsoft.EntityFrameworkCore;
using Reservas.Domain.Reservas;

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
    }
}
