using HotelBookingHub.Comun.Excepciones;
using Hoteles.Domain.Hoteles;

namespace Hoteles.Domain.Puertos;

/// <summary>
/// Puerto de persistencia de hoteles (el dominio define el puerto; el adaptador EF Core vive en
/// infraestructura). En 2.1 la escritura es auto-contenida (un solo INSERT, sin eventos). Cuando 2.5 emita
/// eventos de catálogo, la escritura pasará por el write-path transaccional con outbox (decisión diferida).
/// </summary>
public interface IHotelRepository
{
    /// <summary>Da de alta el hotel y devuelve su <c>rowversion</c> inicial (para que el cliente pueda editar sin re-leer).</summary>
    Task<byte[]> CrearAsync(Hotel hotel, CancellationToken ct);

    /// <summary>
    /// Obtiene un hotel <b>activo</b> por su id, o <c>null</c> si no existe o fue eliminado lógicamente
    /// (el query filter global excluye los eliminados) → el handler responde 404 (AC-E2.2.4).
    /// </summary>
    Task<Hotel?> ObtenerAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Persiste los cambios del hotel usando el <paramref name="rowVersionOriginal"/> que trajo el cliente como
    /// token de <b>concurrencia optimista</b>. Si otro editó/eliminó en medio, el <c>UPDATE</c> afecta 0 filas y
    /// se traduce a <see cref="ConflictoConcurrenciaException"/> (409, sin retry — AC-E2.2.3). Devuelve el
    /// <c>rowversion</c> resultante para exponerlo en la respuesta.
    /// </summary>
    Task<byte[]> GuardarConcurrenciaAsync(Hotel hotel, byte[] rowVersionOriginal, CancellationToken ct);
}
