using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.BuscarDisponibilidad;

namespace Reservas.Infrastructure.Cache;

/// <summary>
/// Caché de disponibilidad sobre Redis vía <see cref="IDistributedCache"/> (AC-E3.2.3). Usa <b>claves
/// generacionales por ciudad</b>: cada entrada se guarda bajo <c>disp:res:{ciudad}:{token}:…</c>, donde
/// <c>token</c> es el valor actual de <c>disp:tok:{ciudad}</c>. Invalidar una ciudad = rotar su token: las
/// entradas viejas quedan huérfanas (expiran por TTL) y la siguiente búsqueda falla en caché y refresca desde el
/// read-model. Es O(1), sin escaneo de claves ni enumeración. El TTL corto acota el <i>staleness</i> por cambios
/// no invalidados de forma dirigida (p. ej. una reserva nueva) — best-effort.
/// </summary>
public sealed class CacheDisponibilidadRedis(IDistributedCache cache, OpcionesCacheDisponibilidad opciones)
    : ICacheDisponibilidad, IInvalidadorCacheDisponibilidad
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<HabitacionDisponibleDto>?> ObtenerAsync(BuscarDisponibilidadQuery consulta, CancellationToken ct)
    {
        var clave = await ClaveEntradaAsync(consulta, ct);
        var bytes = await cache.GetAsync(clave, ct);
        return bytes is null ? null : JsonSerializer.Deserialize<List<HabitacionDisponibleDto>>(bytes, _json);
    }

    public async Task GuardarAsync(BuscarDisponibilidadQuery consulta, IReadOnlyList<HabitacionDisponibleDto> resultado, CancellationToken ct)
    {
        var clave = await ClaveEntradaAsync(consulta, ct);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(resultado, _json);
        await cache.SetAsync(clave, bytes, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = opciones.Ttl }, ct);
    }

    public Task InvalidarCiudadAsync(string ciudad, CancellationToken ct) =>
        // Rota el token de la ciudad: nueva generación → las entradas previas dejan de ser alcanzables.
        cache.SetStringAsync(ClaveTokenCiudad(ciudad), Guid.NewGuid().ToString("N"), ct);

    private static string Normalizar(string ciudad) => ciudad.Trim().ToLowerInvariant();

    private static string ClaveTokenCiudad(string ciudad) => $"disp:tok:{Normalizar(ciudad)}";

    private async Task<string> ClaveEntradaAsync(BuscarDisponibilidadQuery c, CancellationToken ct)
    {
        var token = await cache.GetStringAsync(ClaveTokenCiudad(c.Ciudad), ct) ?? "0";
        return $"disp:res:{Normalizar(c.Ciudad)}:{token}:{c.Entrada:yyyyMMdd}:{c.Salida:yyyyMMdd}:{c.Huespedes}";
    }
}
