using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Reservas.Application.Abstracciones;
using Reservas.Infrastructure.Persistencia;

namespace Reservas.Infrastructure.Mensajeria;

/// <summary>
/// Implementación EF Core de <see cref="IColaOutbox"/>: STAGEA la fila de outbox (serializa el envelope como
/// payload) en el mismo <c>DbContext</c> del cambio de dominio. El <c>MessageId</c> viene del
/// <see cref="ContextoMensajeria"/> (asignado una vez por el <c>TransactionBehavior</c>).
/// </summary>
public sealed class ColaOutbox(ReservasDbContext db, ContextoMensajeria contexto) : IColaOutbox
{
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);

    public void Encolar(string tipo, int version, Guid aggregateId, object data, string? traceId)
    {
        var messageId = contexto.MessageId;
        var ahora = DateTimeOffset.UtcNow;

        var envelope = new EventoIntegracion(messageId, tipo, version, ahora, traceId, data);
        var payload = JsonSerializer.Serialize(envelope, _opciones);

        db.OutboxMessages.Add(OutboxMessage.Crear(messageId, aggregateId, tipo, payload, ahora));
    }
}
