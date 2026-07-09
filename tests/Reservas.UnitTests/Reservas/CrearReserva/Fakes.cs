using HotelBookingHub.Comun.Eventos;
using Reservas.Domain.Puertos;
using Reservas.Domain.Reservas;

namespace Reservas.UnitTests.CrearReserva;

/// <summary>Publisher de eventos fake in-memory (AC-E1.6a.1): captura lo publicado, sin Dapr ni broker.</summary>
public sealed class PublicadorEventosFake : IPublicadorEventos
{
    public List<(string Topico, EventoIntegracion Evento)> Publicados { get; } = [];

    public Task PublicarAsync(string topico, EventoIntegracion evento, CancellationToken ct)
    {
        Publicados.Add((topico, evento));
        return Task.CompletedTask;
    }
}

/// <summary>Repositorio fake: captura las reservas confirmadas; puede simular la carrera perdida (409).</summary>
public sealed class RepositorioReservasFake : IReservaRepository
{
    public List<Reserva> Confirmadas { get; } = [];
    public bool SimularNoDisponible { get; set; }

    public Task ConfirmarAsync(Reserva reserva, CancellationToken ct)
    {
        if (SimularNoDisponible)
        {
            throw new HabitacionNoDisponibleException();
        }

        Confirmadas.Add(reserva);
        return Task.CompletedTask;
    }
}

/// <summary>Disponibilidad sembrada por test: devuelve la habitación configurada (o null = no encontrada).</summary>
public sealed class DisponibilidadHabitacionFake : IDisponibilidadHabitacion
{
    public HabitacionReservable? Habitacion { get; set; }

    public Task<HabitacionReservable?> ObtenerAsync(Guid habitacionId, CancellationToken ct) =>
        Task.FromResult(Habitacion);
}
