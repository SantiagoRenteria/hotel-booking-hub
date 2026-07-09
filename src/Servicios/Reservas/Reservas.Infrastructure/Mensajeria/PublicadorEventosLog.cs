using HotelBookingHub.Comun.Eventos;
using Microsoft.Extensions.Logging;

namespace Reservas.Infrastructure.Mensajeria;

/// <summary>
/// Placeholder de <see cref="IPublicadorEventos"/> que registra el evento en el log. Mantiene el servicio
/// ejecutable end-to-end sin Dapr. En 1.6b lo reemplaza el relay del outbox (tabla → Dapr pub/sub).
/// </summary>
public sealed class PublicadorEventosLog(ILogger<PublicadorEventosLog> logger) : IPublicadorEventos
{
    public Task PublicarAsync(string topico, EventoIntegracion evento, CancellationToken ct)
    {
        logger.LogInformation(
            "Evento {Tipo} (MessageId {MessageId}, Agregado {AggregateId}) listo para {Topico}",
            evento.Type, evento.Id, (evento.Data as ReservaConfirmadaV1)?.AggregateId, topico);
        return Task.CompletedTask;
    }
}
