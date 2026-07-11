using HotelBookingHub.Comun.Excepciones;
using Hoteles.Application.Abstracciones;
using Hoteles.Domain.Hoteles;
using Hoteles.Domain.Puertos;
using Hoteles.Infrastructure.Cache;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Hoteles.Infrastructure.Persistencia;

/// <summary>
/// Adaptador EF Core de <see cref="IHotelRepository"/>. En 2.1 la escritura es auto-contenida (un INSERT sin
/// eventos). En 2.5, cuando el alta emita un evento de catálogo, migrará al write-path transaccional con outbox.
/// La edición/baja (2.2) usan concurrencia optimista por <c>rowversion</c>.
/// </summary>
public sealed class HotelRepository(HotelesDbContext db, IInvalidadorCacheCatalogo? cache = null) : IHotelRepository
{
    // En producción DI inyecta el invalidador (Redis o no-op); en tests que construyen el repo a mano y no
    // ejercen la caché, `null` degrada a no-op (sin Redis no hay nada que invalidar).
    private readonly IInvalidadorCacheCatalogo _cache = cache ?? new InvalidadorCacheCatalogoNoop();

    public async Task<byte[]> CrearAsync(Hotel hotel, CancellationToken ct)
    {
        db.Hoteles.Add(hotel);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (EsViolacionUnicidad(ex))
        {
            // El índice único filtrado (AgentePropietario, Nombre, Ciudad) rechazó un duplicado → 409.
            throw new HotelDuplicadoException(ex);
        }

        // Invalida la lista cacheada del agente (Story T.6) → un GET posterior muestra el alta de inmediato.
        await _cache.InvalidarHotelesDeAgenteAsync(hotel.AgentePropietario, ct);
        return RowVersionActual(hotel);
    }

    public Task<Hotel?> ObtenerAsync(Guid id, CancellationToken ct) =>
        db.Hoteles.FirstOrDefaultAsync(h => h.Id == id, ct);

    public async Task<byte[]> GuardarConcurrenciaAsync(Hotel hotel, byte[] rowVersionOriginal, CancellationToken ct)
    {
        var entrada = db.Entry(hotel);

        // Forzamos el estado Modified: si la edición fuese idempotente (mismos valores), EF no emitiría UPDATE,
        // NO comprobaría el token y una edición con rowVersion obsoleto devolvería 200 en vez de 409 (rompiendo
        // el contrato de concurrencia). Marcarla Modified garantiza siempre el UPDATE con el WHERE del token.
        entrada.State = EntityState.Modified;

        // ...pero Modified marca TODAS las propiedades, incluida la shadow `Seq` (identity clustered, ADR-017):
        // SQL Server rechaza actualizar una columna identity. La excluimos del SET (el rowversion, como token de
        // concurrencia store-generated, EF ya lo excluye del SET y lo usa solo en el WHERE).
        entrada.Property("Seq").IsModified = false;

        // Concurrencia optimista «disconnected»: el token que arbitra la carrera es el rowversion que leyó el
        // CLIENTE (no el recién cargado), de modo que una edición basada en datos obsoletos se rechaza aunque
        // nadie haya tocado la fila entre el load y el save de ESTA request. EF lo pone en el WHERE del UPDATE.
        entrada.Property("RowVersion").OriginalValue = rowVersionOriginal;

        try
        {
            await db.SaveChangesAsync(ct);
            await _cache.InvalidarHotelesDeAgenteAsync(hotel.AgentePropietario, ct); // editar/eliminar/estado → lista caduca
            if (hotel.Eliminado)
            {
                // Al eliminar el hotel, su lista de habitaciones deja de ser visible (query filter) → debe dar 404.
                // La caché de habitaciones se genera por hotelId, así que hay que caducarla también (si no, HIT stale).
                await _cache.InvalidarHabitacionesDeHotelAsync(hotel.Id, ct);
            }

            return RowVersionActual(hotel);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Determinístico: otro modificó/eliminó la fila y el UPDATE afectó 0 filas. Reintentar sin recargar
            // volvería a fallar → 409 y NUNCA retry (a diferencia del deadlock 1205, que sí es transitorio).
            throw new ConflictoConcurrenciaException(ex);
        }
        catch (DbUpdateException ex) when (EsViolacionUnicidad(ex))
        {
            // Renombrar el hotel a un par (Nombre, Ciudad) ya usado por el agente → el índice único lo rechaza.
            throw new HotelDuplicadoException(ex);
        }
    }

    // EF refresca el rowversion (shadow property) tras SaveChanges; lo devolvemos para exponerlo en la respuesta.
    private byte[] RowVersionActual(Hotel hotel) => (byte[])db.Entry(hotel).Property("RowVersion").CurrentValue!;

    // Violación de índice único de SQL Server: 2627 (constraint) / 2601 (índice único). Determinístico → 409.
    private static bool EsViolacionUnicidad(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && sql.Number is 2601 or 2627;
}
