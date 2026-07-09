using Reservas.Application.Reservas.BuscarDisponibilidad;

namespace Reservas.Application.Abstracciones;

/// <summary>
/// Caché de lectura de la búsqueda de disponibilidad (AC-E3.2.3). Cachea el resultado por la clave normalizada
/// <c>(ciudad, entrada, salida, huéspedes)</c> con TTL. El detalle de cómo se materializa la clave física
/// (generacional por ciudad) queda encapsulado en la implementación. La invalidación dirigida por ciudad vive en
/// <see cref="IInvalidadorCacheDisponibilidad"/> (misma implementación Redis).
/// </summary>
public interface ICacheDisponibilidad
{
    /// <summary>
    /// Devuelve el resultado cacheado y vigente; en miss ejecuta <paramref name="calcular"/> (la fuente de verdad),
    /// puebla la caché y lo devuelve. Colapsar obtener+guardar en una sola operación garantiza que la generación
    /// (token de ciudad) se lee UNA vez: una invalidación concurrente no cachea datos viejos bajo el token nuevo.
    /// La implementación degrada a <paramref name="calcular"/> si la caché no está disponible (best-effort).
    /// </summary>
    Task<IReadOnlyList<HabitacionDisponibleDto>> ObtenerOCalcularAsync(
        BuscarDisponibilidadQuery consulta,
        Func<CancellationToken, Task<IReadOnlyList<HabitacionDisponibleDto>>> calcular,
        CancellationToken ct);
}

/// <summary>
/// Invalidación dirigida de la caché de disponibilidad de una ciudad (AC-E3.2.3). La dispara el proyector de
/// catálogo tras aplicar un evento (alta/precio/deshabilitada de habitación, o habilitar/deshabilitar hotel) que
/// afecte a la ciudad, de modo que la siguiente búsqueda refresque el resultado desde el read-model.
/// </summary>
public interface IInvalidadorCacheDisponibilidad
{
    Task InvalidarCiudadAsync(string ciudad, CancellationToken ct);
}
