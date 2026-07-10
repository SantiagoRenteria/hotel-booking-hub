namespace Reservas.Infrastructure.Cache;

/// <summary>
/// Opciones de la caché de lectura de disponibilidad (Story 3.2). El TTL corto acota el <i>staleness</i> por
/// cambios que no invalidamos de forma dirigida (p. ej. una reserva nueva ocupa noches): la búsqueda es
/// best-effort y a lo sumo produce un 409 evitable, nunca overbooking.
/// </summary>
public sealed record OpcionesCacheDisponibilidad
{
    /// <summary>Vigencia de una entrada de caché. Corto por diseño (best-effort).</summary>
    public TimeSpan Ttl { get; init; } = TimeSpan.FromSeconds(30);
}
