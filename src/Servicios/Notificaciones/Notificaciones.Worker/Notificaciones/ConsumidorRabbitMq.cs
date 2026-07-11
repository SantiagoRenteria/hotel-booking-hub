using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Consumidor del transporte real (Story 9.1, ADR-019): se suscribe a una cola DURABLE bound al topic "reservas"
/// del exchange durable, deserializa cada envelope <see cref="EventoIntegracion"/> y lo entrega al
/// <see cref="EnrutadorNotificaciones"/> (que enruta por tipo y aplica idempotencia + dead-letter). ACK manual:
/// solo tras resolver; si el enrutador relanza (aún hay presupuesto de reintentos), NACK+requeue para que el
/// broker reentregue (at-least-once); la no-duplicación la garantiza el inbox idempotente del consumidor.
/// </summary>
public sealed class ConsumidorRabbitMq(string cadenaConexion, EnrutadorNotificaciones enrutador, ILogger<ConsumidorRabbitMq> logger)
    : BackgroundService
{
    // Contrato de transporte: exchange y cola durables con nombres estables (coinciden con PublicadorEventosRabbitMq).
    public const string Exchange = "hotelbookinghub.eventos";
    public const string Cola = "notificaciones.reservas";
    public const string Topico = "reservas";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private IConnection? _conexion;
    private IChannel? _canal;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fabrica = new ConnectionFactory { Uri = new Uri(cadenaConexion) };
        _conexion = await fabrica.CreateConnectionAsync(stoppingToken);
        _canal = await _conexion.CreateChannelAsync(cancellationToken: stoppingToken);

        // Topología durable declarada por el consumidor (idempotente): el exchange, la cola y el binding
        // sobreviven reinicios y existen aunque el productor publique antes de que el consumidor arranque.
        await _canal.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await _canal.QueueDeclareAsync(Cola, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _canal.QueueBindAsync(Cola, Exchange, Topico, cancellationToken: stoppingToken);
        await _canal.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken: stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(_canal);
        consumidor.ReceivedAsync += (_, ea) => ProcesarEntregaAsync(ea, stoppingToken);
        await _canal.BasicConsumeAsync(Cola, autoAck: false, consumidor, cancellationToken: stoppingToken);

        logger.LogInformation("ConsumidorRabbitMq suscrito a la cola {Cola} (topic {Topico})", Cola, Topico);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Apagado normal.
        }
    }

    private async Task ProcesarEntregaAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        if (_canal is null)
        {
            return;
        }

        EventoIntegracion? evento;
        try
        {
            evento = JsonSerializer.Deserialize<EventoIntegracion>(ea.Body.Span, _json);
        }
        catch (JsonException ex)
        {
            // Envelope irrecuperable (no es el contrato): no se puede enrutar ni reintentar → descartar con ACK.
            logger.LogError(ex, "Envelope no deserializable; se descarta (ack)");
            await _canal.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
            return;
        }

        if (evento is null)
        {
            await _canal.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
            return;
        }

        try
        {
            await enrutador.EnrutarAsync(evento, ct);
            await _canal.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El enrutador relanzó → aún hay presupuesto de reintentos: requeue para reentrega (at-least-once).
            // Al agotarse el tope, el DespachadorNotificaciones aparta a dead-letter y NO relanza → se hará ACK.
            logger.LogWarning(ex, "Fallo al procesar el evento {Tipo} ({MessageId}); requeue", evento.Type, evento.Id);
            await _canal.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, ct);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (_canal is not null)
        {
            await _canal.DisposeAsync();
        }

        if (_conexion is not null)
        {
            await _conexion.DisposeAsync();
        }
    }
}
