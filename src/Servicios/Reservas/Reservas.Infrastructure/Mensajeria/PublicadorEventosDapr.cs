using Dapr.Client;
using HotelBookingHub.Comun.Eventos;
using Microsoft.Extensions.Logging;

namespace Reservas.Infrastructure.Mensajeria;

/// <summary>
/// Adaptador de transporte de <see cref="IPublicadorEventos"/> por <b>Dapr pub/sub</b> (ADR-019 — entorno NUBE).
/// Publica el envelope <see cref="EventoIntegracion"/> al component Dapr <c>pubsub</c> (Azure Service Bus en ACA;
/// mismo <c>name</c> que el YAML local), tópico <c>hotelbookinghub-eventos</c>. El adaptador hermano local es
/// <see cref="PublicadorEventosRabbitMq"/>: mismo puerto, distinto proveedor por entorno (Strategy). El <c>topico</c>
/// del puerto (routing key de RabbitMQ) no aplica aquí — todos los eventos van al único tópico de Service Bus y el
/// worker enruta por <see cref="EventoIntegracion.Type"/>. Ante fallo, la excepción se propaga → el
/// <c>ProcesadorOutbox</c> deja la fila Pendiente y reintenta (at-least-once, igual que en RabbitMQ).
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
