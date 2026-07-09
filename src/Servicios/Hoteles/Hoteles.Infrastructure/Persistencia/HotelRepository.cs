using Hoteles.Domain.Hoteles;
using Hoteles.Domain.Puertos;

namespace Hoteles.Infrastructure.Persistencia;

/// <summary>
/// Adaptador EF Core de <see cref="IHotelRepository"/>. En 2.1 la escritura es auto-contenida (un INSERT sin
/// eventos). En 2.5, cuando el alta emita un evento de catálogo, migrará al write-path transaccional con outbox.
/// </summary>
public sealed class HotelRepository(HotelesDbContext db) : IHotelRepository
{
    public async Task CrearAsync(Hotel hotel, CancellationToken ct)
    {
        db.Hoteles.Add(hotel);
        await db.SaveChangesAsync(ct);
    }
}
