using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.BuscarDisponibilidad;

namespace Reservas.Infrastructure.Proyeccion;

/// <summary>
/// Decoradora de caché (AC-E3.2.3) sobre un <see cref="IBuscadorDisponibilidad"/> interno: sirve desde la caché
/// en hit y, en miss, delega en el buscador real y puebla la caché. STUB de la fase RED.
/// </summary>
public sealed class BuscadorDisponibilidadCacheado(IBuscadorDisponibilidad interno, ICacheDisponibilidad cache)
    : IBuscadorDisponibilidad
{
    public Task<IReadOnlyList<HabitacionDisponibleDto>> BuscarAsync(BuscarDisponibilidadQuery consulta, CancellationToken ct) =>
        throw new NotImplementedException(
            $"RED: decoradora de caché pendiente (GREEN). interno={interno.GetType().Name}, cache={cache.GetType().Name}, ciudad={consulta.Ciudad}");
}
