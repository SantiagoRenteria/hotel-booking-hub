using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Consumidor del transporte real (Story 9.1): se suscribe a la cola de notificaciones (bound al topic
/// "reservas" del exchange durable) y entrega cada evento al <see cref="EnrutadorNotificaciones"/>. STUB — la
/// suscripción real se implementa en la fase verde.
/// </summary>
public sealed class ConsumidorRabbitMq(string cadenaConexion, EnrutadorNotificaciones enrutador, ILogger<ConsumidorRabbitMq> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // STUB (fase roja): aún no se suscribe al broker. Referencia las dependencias para dejar constancia
        // de la costura; la suscripción real (declarar cola/binding, consumir, enrutar, ack/nack) es la fase verde.
        logger.LogWarning(
            "ConsumidorRabbitMq STUB: sin suscripción todavía (cadena configurada={Configurada}, enrutador={Enrutador})",
            !string.IsNullOrWhiteSpace(cadenaConexion), enrutador is not null);
        return Task.CompletedTask;
    }
}
