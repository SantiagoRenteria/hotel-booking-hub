using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.BuscarDisponibilidad;

namespace Reservas.Infrastructure.Proyeccion;

/// <summary>
/// Decoradora de caché (AC-E3.2.3) sobre un <see cref="IBuscadorDisponibilidad"/> interno: en HIT sirve desde la
/// caché sin tocar el read-model; en MISS delega en el buscador real y puebla la caché con el resultado. La
/// invalidación dirigida por ciudad vive en la propia caché (la dispara el proyector de catálogo).
/// </summary>
public sealed class BuscadorDisponibilidadCacheado(IBuscadorDisponibilidad interno, ICacheDisponibilidad cache)
    : IBuscadorDisponibilidad
{
    public async Task<IReadOnlyList<HabitacionDisponibleDto>> BuscarAsync(BuscarDisponibilidadQuery consulta, CancellationToken ct)
    {
        var enCache = await cache.ObtenerAsync(consulta, ct);
        if (enCache is not null)
        {
            return enCache;
        }

        var resultado = await interno.BuscarAsync(consulta, ct);
        await cache.GuardarAsync(consulta, resultado, ct);
        return resultado;
    }
}
