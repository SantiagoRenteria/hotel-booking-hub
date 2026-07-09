using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Hoteles.Application.Abstracciones;
using Hoteles.Infrastructure.Persistencia;

namespace Hoteles.Infrastructure.Mensajeria;

/// <summary>
/// Implementación EF Core de <see cref="IColaOutbox"/>: STAGEA la fila de outbox (serializa el envelope como
/// payload) en el mismo <c>DbContext</c> del cambio de dominio. El <c>MessageId</c> (clave de dedup, UNIQUE) se
/// genera aquí por evento: al no haber retry-con-replay (Opción U), no hay riesgo de re-encolar con un id nuevo.
/// Cada comando de habitación emite a lo sumo un evento, así que no hace falta el guard "1 evento por comando".
/// </summary>
public sealed class ColaOutbox(HotelesDbContext db) : IColaOutbox
{
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);

    public void Encolar(string tipo, int version, Guid aggregateId, object data, string? traceId)
    {
        var messageId = Guid.CreateVersion7();
        var ahora = DateTimeOffset.UtcNow;

        var envelope = new EventoIntegracion(messageId, tipo, version, ahora, traceId, data);
        var payload = JsonSerializer.Serialize(envelope, _opciones);

        db.OutboxMessages.Add(OutboxMessage.Crear(messageId, aggregateId, tipo, payload, ahora));
    }
}
