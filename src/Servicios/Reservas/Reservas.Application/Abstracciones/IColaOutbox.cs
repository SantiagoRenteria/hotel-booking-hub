namespace Reservas.Application.Abstracciones;

/// <summary>
/// Puerto para encolar un evento de integración en el Transactional Outbox. La implementación STAGEA la
/// fila (<c>Add</c>) en el mismo <c>DbContext</c> del cambio de dominio — el <c>TransactionBehavior</c> las
/// confirma juntas en un solo <c>SaveChanges</c> (atomicidad, ADR-018). El <c>MessageId</c> lo asigna la
/// unidad de trabajo una sola vez (estable ante el retry 1205), NO el handler.
/// </summary>
public interface IColaOutbox
{
    void Encolar(string tipo, int version, Guid aggregateId, object data, string? traceId);
}
