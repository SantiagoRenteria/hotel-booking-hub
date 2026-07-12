namespace Hoteles.Application.Abstracciones;

/// <summary>
/// Invalida la caché de lectura del catálogo tras una escritura (Story T.6). Estrategia de **generación**: cada
/// invalidación incrementa un contador por clave lógica; las claves de caché incluyen la generación vigente, así
/// que un bump vuelve inalcanzables las páginas cacheadas viejas (miss → SQL fresco) sin borrado por patrón.
/// Lo llaman los repositorios tras un SaveChanges exitoso. Implementación no-op cuando no hay Redis.
/// </summary>
public interface IInvalidadorCacheCatalogo
{
    /// <summary>Tras crear/editar/eliminar/habilitar/deshabilitar un hotel del agente → su lista de hoteles caduca.</summary>
    Task InvalidarHotelesDeAgenteAsync(string agente, CancellationToken ct);

    /// <summary>Tras crear/editar/cambiar-estado una habitación → la lista de habitaciones de su hotel caduca.</summary>
    Task InvalidarHabitacionesDeHotelAsync(Guid hotelId, CancellationToken ct);
}
