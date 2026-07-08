namespace HotelBookingHub.Comun.Eventos;

/// <summary>
/// Envelope de un evento de integración entre Bounded Contexts.
/// Forma mínima del walking skeleton (Story 1.1); el contrato definitivo —con
/// clave de deduplicación y de orden congeladas y su contract test— se formaliza
/// en la Story 1.3. La comunicación entre BCs es solo por eventos (ADR-002).
/// </summary>
/// <param name="Id">Identificador único del mensaje (dedup key; UUID v7).</param>
/// <param name="Type">Tipo del evento en PascalCase español + semver, p. ej. "ReservaConfirmada.v1".</param>
/// <param name="Version">Versión de secuencia del agregado (order key).</param>
/// <param name="OccurredAt">Instante en que ocurrió (siempre <see cref="DateTimeOffset"/>).</param>
/// <param name="TraceId">Trace-id W3C propagado (correlación de negocio); nulo si no hay actividad.</param>
/// <param name="Data">Carga útil serializable del evento.</param>
public sealed record EventoIntegracion(
    Guid Id,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    string? TraceId,
    object Data);
