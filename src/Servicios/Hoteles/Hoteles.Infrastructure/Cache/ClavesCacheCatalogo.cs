namespace Hoteles.Infrastructure.Cache;

/// <summary>
/// Esquema de claves de la caché del catálogo (Story T.6). Compartido por el lector cacheado (arma la clave con
/// la generación vigente) y el invalidador (bump de la clave-generación). Convención de generación: la lista se
/// cachea en <c>hoteles:{agente}:g{gen}:p{page}:s{size}</c> y la generación vive en <c>hoteles-gen:{agente}</c>;
/// al invalidar se hace <c>INCR</c> de la clave-gen → las claves con la gen anterior quedan huérfanas (expiran
/// por TTL). Ídem habitaciones por <c>{hotelId}</c>.
/// </summary>
internal static class ClavesCacheCatalogo
{
    public static string GenHoteles(string agente) => $"catalogo:hoteles-gen:{agente}";

    public static string GenHabitaciones(Guid hotelId) => $"catalogo:habitaciones-gen:{hotelId}";

    public static string Hoteles(string agente, long gen, int page, int pageSize) =>
        $"catalogo:hoteles:{agente}:g{gen}:p{page}:s{pageSize}";

    // El AGENTE va en la clave (aunque la generación sea por hotelId): sin él, un hit cacheado por el dueño se
    // serviría a CUALQUIER agente que pida el mismo hotelId, saltándose la comprobación de propiedad del inner
    // (IDOR). Con el agente en la clave, un no-dueño arma una clave distinta → miss → inner → 404 (no se cachea).
    public static string Habitaciones(string agente, Guid hotelId, long gen, int page, int pageSize) =>
        $"catalogo:habitaciones:{agente}:{hotelId}:g{gen}:p{page}:s{pageSize}";
}

/// <summary>TTL de respaldo de las páginas cacheadas (la corrección la da la invalidación por generación; el TTL solo acota memoria).</summary>
public sealed class OpcionesCacheCatalogo
{
    public TimeSpan Ttl { get; init; } = TimeSpan.FromSeconds(60);
}
