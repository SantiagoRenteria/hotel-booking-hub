using HotelBookingHub.Comun.Eventos;
using Microsoft.EntityFrameworkCore;
using Reservas.Domain.Reservas;
using Reservas.Infrastructure.Mensajeria;
using Reservas.Infrastructure.Persistencia;

namespace Reservas.IntegrationTests;

/// <summary>
/// Escritura atómica reserva + outbox tal como la ejecuta el handler en producción: stagear vía el
/// repositorio + la cola de outbox y confirmar con el <see cref="EjecutorTransaccional"/> (un solo
/// SaveChanges). Aísla los tests limpiando las tablas cuando comparten contenedor.
/// </summary>
internal static class HelpersOutbox
{
    public static Reserva NuevaReserva() =>
        Reserva.Crear(Guid.NewGuid(), Estancia.Crear(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 12)));

    public static async Task ConfirmarConOutboxAsync(ReservasDbContext db, Reserva reserva, Guid messageId, CancellationToken ct)
    {
        var contexto = new ContextoMensajeria { MessageId = messageId };
        var repositorio = new ReservaRepository(db);
        var cola = new ColaOutbox(db, contexto);
        var ejecutor = new EjecutorTransaccional(db);

        await ejecutor.EjecutarAsync(
            _ =>
            {
                repositorio.Agregar(reserva);
                cola.Encolar(ReservaConfirmadaV1.Tipo, 1, reserva.Id, DatosEvento(reserva), traceId: null);
                return Task.CompletedTask;
            },
            ct);
    }

    public static Task LimpiarAsync(ReservasDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            "DELETE FROM OutboxMessages; DELETE FROM NochesHabitacion; DELETE FROM Reservas;", ct);

    private static ReservaConfirmadaV1 DatosEvento(Reserva r) => new(
        AggregateId: r.Id,
        HotelId: Guid.NewGuid(),
        HotelNombre: "Hotel Demo",
        Ciudad: "Bogotá",
        HabitacionId: r.HabitacionId,
        Entrada: r.Estancia.Entrada,
        Salida: r.Estancia.Salida,
        HuespedNombre: "Juan García",
        HuespedEmail: "juan@correo.com",
        AgenteEmail: "agente@hotel.com",
        PrecioTotal: 240.98m);
}
