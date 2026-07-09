using Reservas.Application.Reservas.BuscarDisponibilidad;

namespace Reservas.Application.Abstracciones;

/// <summary>
/// Caché de lectura de la búsqueda de disponibilidad (AC-E3.2.3). Cachea el resultado por la clave normalizada
/// <c>(ciudad, entrada, salida, huéspedes)</c> con TTL. La invalidación dirigida por ciudad vive en
/// <see cref="IInvalidadorCacheDisponibilidad"/> (misma implementación Redis). El detalle de cómo se materializa
/// la clave física (generacional por ciudad) queda encapsulado en la implementación.
/// </summary>
public interface ICacheDisponibilidad
{
    /// <summary>Devuelve el resultado cacheado y vigente, o <c>null</c> si no hay entrada (miss).</summary>
    Task<IReadOnlyList<HabitacionDisponibleDto>?> ObtenerAsync(BuscarDisponibilidadQuery consulta, CancellationToken ct);

    /// <summary>Guarda el resultado con el TTL configurado bajo la clave de la consulta.</summary>
    Task GuardarAsync(BuscarDisponibilidadQuery consulta, IReadOnlyList<HabitacionDisponibleDto> resultado, CancellationToken ct);
}

/// <summary>
/// Invalidación dirigida de la caché de disponibilidad de una ciudad (AC-E3.2.3). La dispara el proyector de
/// catálogo tras aplicar un evento (alta/precio/deshabilitada) que afecte a la ciudad, de modo que la siguiente
/// búsqueda refresque el resultado desde el read-model.
/// </summary>
public interface IInvalidadorCacheDisponibilidad
{
    Task InvalidarCiudadAsync(string ciudad, CancellationToken ct);
}
