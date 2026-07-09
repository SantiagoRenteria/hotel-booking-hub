using HotelBookingHub.Comun.Excepciones;
using Reservas.Domain.Reservas;
using Reservas.Infrastructure.Persistencia;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Story 4.3 (Task 0) — el <see cref="EjecutorTransaccional"/> distingue el árbitro de dominio (overbooking) de
/// otras violaciones de único por la ENTIDAD ofensora (sin parsear el mensaje). Tests GEMELOS: una colisión en
/// <c>NochesHabitacion</c> → <see cref="HabitacionNoDisponibleException"/> (409); una colisión de
/// <c>UNIQUE(OutboxMessages.MessageId)</c> → excepción NO de negocio (→ 500), nunca 409. Esto permite el outbox
/// multi-evento sin que un bug de id se disfrace de overbooking. Se fuerza el 2627/2601 REAL con dos
/// transacciones secuenciales (una sola SaveChanges con PK duplicada la atajaría el ChangeTracker antes de SQL).
/// </summary>
[Collection("sqlserver")]
public sealed class OutboxClasificacionConflictoTests(SqlServerFixture fixture)
{
    private async Task EjecutarAsync(Func<ReservasDbContext, Task> accion)
    {
        await using var db = fixture.CrearContexto();
        var ejecutor = new EjecutorTransaccional(db);
        await ejecutor.EjecutarAsync(_ => accion(db), CancellationToken.None);
    }

    // GEMELO 1: colisión del árbitro anti-overbooking (dos reservas sobre la misma noche) → 409.
    [Fact]
    public async Task Colision_de_noche_se_traduce_a_HabitacionNoDisponible()
    {
        var habitacion = Guid.NewGuid();
        var estancia = Estancia.Crear(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2)); // 1 noche

        await EjecutarAsync(db =>
        {
            new ReservaRepository(db).Agregar(Reserva.Crear(habitacion, estancia));
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<HabitacionNoDisponibleException>(() => EjecutarAsync(db =>
        {
            new ReservaRepository(db).Agregar(Reserva.Crear(habitacion, estancia)); // misma noche → 2627
            return Task.CompletedTask;
        }));
    }

    // GEMELO 2: colisión del MessageId del outbox → NO es de negocio (no 409). Sería un bug, nunca overbooking.
    [Fact]
    public async Task Colision_de_MessageId_del_outbox_no_se_traduce_a_conflicto_de_negocio()
    {
        var messageId = Guid.CreateVersion7();

        await EjecutarAsync(db =>
        {
            db.OutboxMessages.Add(OutboxMessage.Crear(messageId, Guid.NewGuid(), "EventoA.v1", "{}", DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        });

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => EjecutarAsync(db =>
        {
            db.OutboxMessages.Add(OutboxMessage.Crear(messageId, Guid.NewGuid(), "EventoB.v1", "{}", DateTimeOffset.UtcNow)); // MessageId duplicado
            return Task.CompletedTask;
        }));

        // NO debe disfrazarse de overbooking/409; es un fallo no-de-negocio (→ 500).
        Assert.IsNotType<HabitacionNoDisponibleException>(ex);
        Assert.IsNotAssignableFrom<ExcepcionNegocio>(ex);
    }

    // GEMELO 2b — batch MIXTO como el del atajo: una noche en Deleted + colisión de MessageId del outbox. La
    // clasificación NO debe confundir el 2601 del outbox con overbooking solo porque hay una NocheHabitacion en
    // el batch (debe mirar el estado Added, no la mera presencia).
    [Fact]
    public async Task Colision_de_outbox_con_una_noche_borrada_en_el_batch_no_es_conflicto_de_negocio()
    {
        // Semilla: una reserva de 1 noche persistida (para poder borrar su noche luego).
        var habitacion = Guid.NewGuid();
        var estancia = Estancia.Crear(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 21));
        Reserva reserva = Reserva.Crear(habitacion, estancia);
        await EjecutarAsync(db =>
        {
            new ReservaRepository(db).Agregar(reserva);
            return Task.CompletedTask;
        });

        var messageId = Guid.CreateVersion7();
        await EjecutarAsync(db =>
        {
            db.OutboxMessages.Add(OutboxMessage.Crear(messageId, Guid.NewGuid(), "EventoA.v1", "{}", DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        });

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => EjecutarAsync(db =>
        {
            // Borra la noche (Deleted, vía stub adjuntado por PK) Y provoca la colisión de MessageId (Added) en la
            // MISMA tx → el batch mezcla NocheHabitacion(Deleted) + OutboxMessage(Added).
            var stub = new NocheHabitacion(habitacion, estancia.Entrada, reserva.Id);
            db.NochesHabitacion.Attach(stub);
            db.NochesHabitacion.Remove(stub);
            db.OutboxMessages.Add(OutboxMessage.Crear(messageId, Guid.NewGuid(), "EventoB.v1", "{}", DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }));

        Assert.IsNotType<HabitacionNoDisponibleException>(ex); // no es overbooking pese a la noche en el batch
        Assert.IsNotAssignableFrom<ExcepcionNegocio>(ex);
    }
}
