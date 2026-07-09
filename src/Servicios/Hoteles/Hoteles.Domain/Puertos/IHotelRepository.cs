using Hoteles.Domain.Hoteles;

namespace Hoteles.Domain.Puertos;

/// <summary>
/// Puerto de persistencia de hoteles (el dominio define el puerto; el adaptador EF Core vive en
/// infraestructura). En 2.1 la escritura es auto-contenida (un solo INSERT, sin eventos). Cuando 2.5 emita
/// eventos de catálogo, la escritura pasará por el write-path transaccional con outbox (decisión diferida).
/// </summary>
public interface IHotelRepository
{
    Task CrearAsync(Hotel hotel, CancellationToken ct);
}
