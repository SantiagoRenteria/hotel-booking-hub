using HotelBookingHub.Comun.Excepciones;
using Hoteles.Domain.Habitaciones;

namespace Hoteles.Domain.Puertos;

/// <summary>
/// Puerto de persistencia de habitaciones. Mismo patrón que <see cref="IHotelRepository"/>: alta que devuelve el
/// <c>rowversion</c> inicial, obtención por id y guardado con concurrencia optimista.
/// </summary>
public interface IHabitacionRepository
{
    /// <summary>Da de alta la habitación y devuelve su <c>rowversion</c> inicial.</summary>
    Task<byte[]> CrearAsync(Habitacion habitacion, CancellationToken ct);

    /// <summary>Obtiene la habitación por su id, o <c>null</c> si no existe (→ 404).</summary>
    Task<Habitacion?> ObtenerAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Persiste los cambios usando el <paramref name="rowVersionOriginal"/> del cliente como token de
    /// concurrencia optimista (conflicto → <see cref="ConflictoConcurrenciaException"/> → 409, sin retry).
    /// Devuelve el <c>rowversion</c> resultante.
    /// </summary>
    Task<byte[]> GuardarConcurrenciaAsync(Habitacion habitacion, byte[] rowVersionOriginal, CancellationToken ct);
}
