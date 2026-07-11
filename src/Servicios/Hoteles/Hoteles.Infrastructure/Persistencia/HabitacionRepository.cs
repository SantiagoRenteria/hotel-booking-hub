using HotelBookingHub.Comun.Excepciones;
using Hoteles.Application.Abstracciones;
using Hoteles.Domain.Habitaciones;
using Hoteles.Domain.Puertos;
using Hoteles.Infrastructure.Cache;
using Microsoft.EntityFrameworkCore;

namespace Hoteles.Infrastructure.Persistencia;

/// <summary>
/// Adaptador EF Core de <see cref="IHabitacionRepository"/>. Reutiliza el mismo mecanismo de concurrencia
/// optimista que <see cref="HotelRepository"/>: fuerza el UPDATE (excluyendo la shadow <c>Seq</c> identity),
/// usa el <c>rowversion</c> del cliente como token y traduce <c>DbUpdateConcurrencyException</c> → 409.
/// </summary>
public sealed class HabitacionRepository(HotelesDbContext db, IInvalidadorCacheCatalogo? cache = null) : IHabitacionRepository
{
    // DI inyecta el invalidador real (Redis/no-op); tests que construyen el repo a mano → null degrada a no-op.
    private readonly IInvalidadorCacheCatalogo _cache = cache ?? new InvalidadorCacheCatalogoNoop();

    public async Task<byte[]> CrearAsync(Habitacion habitacion, CancellationToken ct)
    {
        db.Habitaciones.Add(habitacion);
        await db.SaveChangesAsync(ct);
        await _cache.InvalidarHabitacionesDeHotelAsync(habitacion.HotelId, ct); // Story T.6: la lista del hotel caduca
        return RowVersionActual(habitacion);
    }

    public Task<Habitacion?> ObtenerAsync(Guid id, CancellationToken ct) =>
        db.Habitaciones.FirstOrDefaultAsync(h => h.Id == id, ct);

    public async Task<byte[]> GuardarConcurrenciaAsync(Habitacion habitacion, byte[] rowVersionOriginal, CancellationToken ct)
    {
        var entrada = db.Entry(habitacion);

        // Fuerza el UPDATE aunque la edición sea idempotente (así el token siempre se comprueba → 409), pero
        // sin marcar la shadow `Seq` (identity), que SQL Server no permite actualizar. Ver HotelRepository.
        entrada.State = EntityState.Modified;
        entrada.Property("Seq").IsModified = false;
        entrada.Property("RowVersion").OriginalValue = rowVersionOriginal;

        try
        {
            await db.SaveChangesAsync(ct);
            await _cache.InvalidarHabitacionesDeHotelAsync(habitacion.HotelId, ct); // editar/estado → lista del hotel caduca
            return RowVersionActual(habitacion);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictoConcurrenciaException(ex);
        }
    }

    private byte[] RowVersionActual(Habitacion habitacion) =>
        (byte[])db.Entry(habitacion).Property("RowVersion").CurrentValue!;
}
