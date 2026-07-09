using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.BuscarDisponibilidad;

namespace Reservas.Infrastructure.Proyeccion;

/// <summary>
/// Decoradora de caché (AC-E3.2.3) sobre un <see cref="IBuscadorDisponibilidad"/> interno: sirve desde la caché
/// en hit y, en miss, delega en el buscador real y puebla la caché — todo dentro de una sola operación de la
/// caché (<see cref="ICacheDisponibilidad.ObtenerOCalcularAsync"/>) que lee la generación una única vez y degrada
/// a la fuente de verdad si Redis no está disponible.
/// </summary>
public sealed class BuscadorDisponibilidadCacheado(IBuscadorDisponibilidad interno, ICacheDisponibilidad cache)
    : IBuscadorDisponibilidad
{
    public Task<IReadOnlyList<HabitacionDisponibleDto>> BuscarAsync(BuscarDisponibilidadQuery consulta, CancellationToken ct) =>
        cache.ObtenerOCalcularAsync(consulta, tokenCt => interno.BuscarAsync(consulta, tokenCt), ct);
}
