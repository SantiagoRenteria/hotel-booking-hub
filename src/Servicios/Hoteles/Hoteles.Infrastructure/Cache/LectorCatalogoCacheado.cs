using System.Text.Json;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Abstracciones;
using Hoteles.Application.Habitaciones;
using Hoteles.Application.Hoteles;
using StackExchange.Redis;

namespace Hoteles.Infrastructure.Cache;

/// <summary>
/// Decorador de <see cref="ILectorCatalogo"/> que cachea las LISTAS en Redis (cache-aside, Story T.6). La clave
/// incluye la generación vigente (ver <see cref="ClavesCacheCatalogo"/>): un bump del invalidador tras una
/// escritura deja las páginas viejas inalcanzables → la próxima lectura hace miss y va a SQL fresco (anti-stale).
/// Los DETALLES (item por PK) pasan directo al inner (ya son baratos). Se registra en lugar del inner cuando hay
/// Redis; sin Redis, el inner (<see cref="LectorCatalogoSql"/>) se usa directo (sin caché).
/// </summary>
public sealed class LectorCatalogoCacheado(
    ILectorCatalogo inner,
    IConnectionMultiplexer redis,
    IContextoAgente contexto,
    OpcionesCacheCatalogo opciones) : ILectorCatalogo
{
    public async Task<PaginaDto<HotelVistaDto>> ListarHotelesAsync(int page, int pageSize, CancellationToken ct)
    {
        var agente = contexto.AgenteActual;
        if (string.IsNullOrWhiteSpace(agente))
        {
            return await inner.ListarHotelesAsync(page, pageSize, ct); // sin identidad no se cachea (el handler ya hace 403)
        }

        var db = redis.GetDatabase();
        var gen = (long?)await db.StringGetAsync(ClavesCacheCatalogo.GenHoteles(agente)) ?? 0L;
        var clave = ClavesCacheCatalogo.Hoteles(agente, gen, page, pageSize);

        var cacheado = await db.StringGetAsync(clave);
        if (cacheado.HasValue)
        {
            return JsonSerializer.Deserialize<PaginaDto<HotelVistaDto>>((string)cacheado!)!;
        }

        var pagina = await inner.ListarHotelesAsync(page, pageSize, ct);
        await db.StringSetAsync(clave, JsonSerializer.Serialize(pagina), opciones.Ttl);
        return pagina;
    }

    public Task<HotelVistaDto?> ObtenerHotelAsync(Guid id, CancellationToken ct) => inner.ObtenerHotelAsync(id, ct);

    public async Task<PaginaDto<HabitacionResponseDto>?> ListarHabitacionesDeHotelAsync(Guid hotelId, int page, int pageSize, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var gen = (long?)await db.StringGetAsync(ClavesCacheCatalogo.GenHabitaciones(hotelId)) ?? 0L;
        var clave = ClavesCacheCatalogo.Habitaciones(hotelId, gen, page, pageSize);

        var cacheado = await db.StringGetAsync(clave);
        if (cacheado.HasValue)
        {
            return JsonSerializer.Deserialize<PaginaDto<HabitacionResponseDto>>((string)cacheado!)!;
        }

        var pagina = await inner.ListarHabitacionesDeHotelAsync(hotelId, page, pageSize, ct);
        if (pagina is null)
        {
            return null; // hotel ajeno/inexistente → 404; no se cachea el "no encontrado"
        }

        await db.StringSetAsync(clave, JsonSerializer.Serialize(pagina), opciones.Ttl);
        return pagina;
    }

    public Task<HabitacionResponseDto?> ObtenerHabitacionAsync(Guid id, CancellationToken ct) => inner.ObtenerHabitacionAsync(id, ct);
}
