using Reservas.Application.Abstracciones;
using Reservas.Domain.Puertos;
using Reservas.Domain.Reservas;

namespace Reservas.UnitTests.CrearReserva;

/// <summary>Cola de outbox fake (AC-E1.6a.1): captura lo encolado, sin BD ni Dapr.</summary>
public sealed class ColaOutboxFake : IColaOutbox
{
    public List<(string Tipo, int Version, Guid AggregateId, object Data, string? TraceId)> Encolados { get; } = [];

    public void Encolar(string tipo, int version, Guid aggregateId, object data, string? traceId) =>
        Encolados.Add((tipo, version, aggregateId, data, traceId));
}

/// <summary>Repositorio fake: captura las reservas STAGEADAS (Agregar no guarda ni arbitra — eso vive en el motor).</summary>
public sealed class RepositorioReservasFake : IReservaRepository
{
    public List<Reserva> Agregadas { get; } = [];

    /// <summary>Reservas ya persistidas que <see cref="ObtenerAsync"/> puede devolver (sembradas por el test).</summary>
    public List<Reserva> Existentes { get; } = [];

    public void Agregar(Reserva reserva) => Agregadas.Add(reserva);

    public Task<Reserva?> ObtenerAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Existentes.Concat(Agregadas).FirstOrDefault(r => r.Id == id));

    // En memoria el agregado ya trae sus Noches (Reserva.Crear las genera), así que es equivalente a ObtenerAsync.
    public Task<Reserva?> ObtenerConNochesAsync(Guid id, CancellationToken ct) => ObtenerAsync(id, ct);
}

/// <summary>Disponibilidad sembrada por test: devuelve la habitación configurada (o null = no encontrada).</summary>
public sealed class DisponibilidadHabitacionFake : IDisponibilidadHabitacion
{
    public HabitacionReservable? Habitacion { get; set; }

    public Task<HabitacionReservable?> ObtenerAsync(Guid habitacionId, CancellationToken ct) =>
        Task.FromResult(Habitacion);
}
