using Hoteles.Application.Abstracciones;
using Hoteles.Application.Habitaciones;
using Hoteles.Application.Hoteles;
using Microsoft.EntityFrameworkCore;

namespace Hoteles.Infrastructure.Persistencia;

/// <summary>
/// Lectura del catálogo del agente (Story T.5), read-side de CQRS. El aislamiento de HOTELES es server-side por
/// el query filter global de <see cref="HotelesDbContext"/> (por <c>AgentePropietario</c> + excluye eliminados),
/// así que un hotel ajeno/eliminado es invisible → detalle null (→404), lista solo lo propio. Las HABITACIONES
/// no tienen filtro propio (la entidad no lleva identidad de agente): su aislamiento es transitivo y se verifica
/// contra el hotel dueño (si el hotel no es del agente → null → 404). Las entidades se leen tracked para exponer
/// el <c>rowversion</c> (shadow property) en el DTO, de modo que el cliente pueda editar tras leer (GET → PUT).
/// </summary>
public sealed class LectorCatalogoSql(HotelesDbContext db) : ILectorCatalogo
{
    public async Task<IReadOnlyList<HotelVistaDto>> ListarHotelesAsync(CancellationToken ct)
    {
        var hoteles = await db.Hoteles.OrderBy(h => h.Nombre).ToListAsync(ct);
        return hoteles.Select(h => HotelVistaDto.De(h, RowVersion(h))).ToList();
    }

    public async Task<HotelVistaDto?> ObtenerHotelAsync(Guid id, CancellationToken ct)
    {
        var hotel = await db.Hoteles.FirstOrDefaultAsync(h => h.Id == id, ct);
        return hotel is null ? null : HotelVistaDto.De(hotel, RowVersion(hotel));
    }

    public async Task<IReadOnlyList<HabitacionResponseDto>?> ListarHabitacionesDeHotelAsync(Guid hotelId, CancellationToken ct)
    {
        // El hotel debe pertenecer al agente (query filter): si no, null → 404 (no una lista vacía que confirme
        // la existencia de un hotel ajeno).
        var esPropio = await db.Hoteles.AnyAsync(h => h.Id == hotelId, ct);
        if (!esPropio)
        {
            return null;
        }

        var habitaciones = await db.Habitaciones.Where(h => h.HotelId == hotelId).OrderBy(h => h.Ubicacion).ToListAsync(ct);
        return habitaciones.Select(h => HabitacionResponseDto.De(h, RowVersion(h))).ToList();
    }

    public async Task<HabitacionResponseDto?> ObtenerHabitacionAsync(Guid id, CancellationToken ct)
    {
        // Habitacion no tiene query filter → se obtiene sin aislamiento y se verifica el hotel dueño aparte.
        var habitacion = await db.Habitaciones.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (habitacion is null)
        {
            return null;
        }

        var hotelPropio = await db.Hoteles.AnyAsync(h => h.Id == habitacion.HotelId, ct);
        return hotelPropio ? HabitacionResponseDto.De(habitacion, RowVersion(habitacion)) : null;
    }

    private byte[] RowVersion(object entidad) => (byte[])db.Entry(entidad).Property("RowVersion").CurrentValue!;
}
