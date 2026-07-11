using Dapr.Client;
using HotelBookingHub.Comun.Eventos;
using Microsoft.Extensions.Logging;

namespace Hoteles.Infrastructure.Mensajeria;

/// <summary>
/// Adaptador de transporte de <see cref="IPublicadorEventos"/> por <b>Dapr pub/sub</b> (ADR-019 — entorno NUBE).
/// Publica el envelope <see cref="EventoIntegracion"/> al component Dapr <c>pubsub</c> (Azure Service Bus en ACA),
/// tópico <c>hotelbookinghub-eventos</c>. El adaptador hermano local es <see cref="PublicadorEventosRabbitMq"/>:
/// mismo puerto, distinto proveedor por entorno (Strategy). El worker enruta por <see cref="EventoIntegracion.Type"/>.
/// Ante fallo la excepción se propaga → el relay del outbox reintenta (at-least-once).
/// </summary>
public sealed class PublicadorEventosDapr(DaprClient dapr, ILogger<PublicadorEventosDapr> logger) : IPublicadorEventos
{
    public const string NombrePubSub = "pubsub";
    public const string Topico = "hotelbookinghub-eventos";

    public async Task PublicarAsync(string topico, EventoIntegracion evento, CancellationToken ct)
    {
        await dapr.PublishEventAsync(NombrePubSub, Topico, evento, ct);
        logger.LogInformation(
            "Evento {Tipo} (MessageId {MessageId}) publicado por Dapr [{PubSub}/{Topico}]",
            evento.Type, evento.Id, NombrePubSub, Topico);
    }
}
