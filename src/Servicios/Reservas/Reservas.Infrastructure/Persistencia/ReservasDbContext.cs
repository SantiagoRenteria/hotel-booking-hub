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

            // Story 3.3: datos de negocio persistidos (antes solo iban al evento).
            b.Property(r => r.AgenteEmail).HasMaxLength(256); // eje de aislamiento del listado (AC-E3.3.2)
            b.Property(r => r.PrecioTotal).HasPrecision(18, 2);

            // Estancia como owned type (columnas Entrada/Salida); Noches es computado → ignorar.
            b.OwnsOne(r => r.Estancia, e =>
            {
                e.Property(x => x.Entrada).HasColumnName("Entrada");
                e.Property(x => x.Salida).HasColumnName("Salida");
                e.Ignore(x => x.Noches);
            });

            // Contacto de emergencia (FR-11) como owned reference (columnas en la propia tabla Reservas).
            b.OwnsOne(r => r.ContactoEmergencia, c =>
            {
                c.Property(x => x.NombreCompleto).HasColumnName("ContactoNombreCompleto").HasMaxLength(200);
                c.Property(x => x.Telefono).HasColumnName("ContactoTelefono").HasMaxLength(40);
            });

            // Solicitud de cancelación (Story 4.1) como owned reference NULLABLE: columnas en la propia tabla
            // Reservas, todas nulas mientras la reserva no tenga un episodio de cancelación. Un solo episodio
            // (4.2 completará esta misma owned con la resolución — decisión party-mode Task 0).
            b.OwnsOne(r => r.SolicitudCancelacion, s =>
            {
                s.Property(x => x.MotivoCategoria).HasColumnName("CancelacionMotivoCategoria").HasMaxLength(80);
                s.Property(x => x.MotivoDetalle).HasColumnName("CancelacionMotivoDetalle").HasMaxLength(1000);
                s.Property(x => x.IniciadaPor).HasColumnName("CancelacionIniciadaPor").HasConversion<string>().HasMaxLength(20);
                s.Property(x => x.PenalidadPorcentaje).HasColumnName("CancelacionPenalidadPorcentaje").HasPrecision(5, 2);
                s.Property(x => x.FechaSolicitud).HasColumnName("CancelacionFechaSolicitud");
            });

            // Huéspedes (FR-10) como owned collection en su propia tabla; Documento es un VO anidado.
            b.OwnsMany(r => r.Huespedes, h =>
            {
                h.ToTable("ReservaHuespedes");
                h.WithOwner().HasForeignKey("ReservaId");
                h.Property(x => x.Nombres).HasMaxLength(120);
                h.Property(x => x.Apellidos).HasMaxLength(120);
                h.Property(x => x.Genero).HasMaxLength(40);
                h.Property(x => x.Email).HasMaxLength(256);
                h.Property(x => x.Telefono).HasMaxLength(40);
                h.OwnsOne(x => x.Documento, d =>
                {
                    d.Property(x => x.Tipo).HasColumnName("DocumentoTipo").HasMaxLength(40);
                    d.Property(x => x.Numero).HasColumnName("DocumentoNumero").HasMaxLength(60);
                });
            });
            b.Navigation(r => r.Huespedes).UsePropertyAccessMode(PropertyAccessMode.Field);

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
