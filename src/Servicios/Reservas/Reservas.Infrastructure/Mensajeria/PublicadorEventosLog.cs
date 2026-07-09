using HotelBookingHub.Comun.Eventos;
using Microsoft.Extensions.Logging;

namespace Reservas.Infrastructure.Mensajeria;

/// <summary>
/// Placeholder de <see cref="IPublicadorEventos"/> que registra el evento en el log. Mantiene el servicio
/// ejecutable end-to-end sin Dapr; lo reemplaza el transporte real (Dapr pub/sub) más adelante.
/// Loguea solo campos ESTABLES del envelope (tras pasar por el outbox, <c>Data</c> llega como
/// <c>JsonElement</c>, no como el tipo concreto — no se debe castear aquí).
/// </summary>
public sealed class PublicadorEventosLog(ILogger<PublicadorEventosLog> logger) : IPublicadorEventos
{
    public Task PublicarAsync(string topico, EventoIntegracion evento, CancellationToken ct)
    {
        logger.LogInformation(
            "Evento {Tipo} (MessageId {MessageId}, Version {Version}) listo para {Topico}",
            evento.Type, evento.Id, evento.Version, topico);
        return Task.CompletedTask;
    }
}
