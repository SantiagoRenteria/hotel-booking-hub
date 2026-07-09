namespace Hoteles.Application.Abstracciones;

/// <summary>
/// Puerto para encolar un evento de integración en el Transactional Outbox del BC de Hoteles. La implementación
/// STAGEA la fila (<c>Add</c>) en el MISMO <c>DbContext</c> del cambio de dominio; el repositorio las confirma
/// juntas en un ÚNICO <c>SaveChanges</c> (atomicidad, AC-E2.5.2 · decisión Opción U del party-mode). A
/// diferencia de Reservas, Hoteles NO difiere el <c>SaveChanges</c> a un <c>TransactionBehavior</c> (baja
/// contención, y así preserva el contrato de <c>rowVersion</c> post-escritura de las respuestas).
/// </summary>
public interface IColaOutbox
{
    /// <param name="tipo">Tipo del evento (PascalCase + semver), p. ej. <c>HabitacionAgregada.v1</c>.</param>
    /// <param name="version">Parte monotónica del order key (<c>Habitacion.Version</c>).</param>
    /// <param name="aggregateId">HabitacionId — parte de identidad del order key.</param>
    /// <param name="data">Carga útil del evento (record de <c>Comun.Eventos</c>).</param>
    /// <param name="traceId">Trace-id W3C para correlación; nulo si no hay actividad.</param>
    void Encolar(string tipo, int version, Guid aggregateId, object data, string? traceId);
}
