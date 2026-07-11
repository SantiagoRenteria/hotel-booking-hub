using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Reservas.Infrastructure.Mensajeria;

/// <summary>
/// Adaptador de transporte REAL de <see cref="IPublicadorEventos"/> por RabbitMQ (Story 9.1, ADR-019 — entorno
/// local). Publica el envelope <see cref="EventoIntegracion"/> serializado a JSON en un exchange direct durable,
/// con routing key = <c>topico</c> ("reservas"). Declara también la cola durable del consumidor + binding para
/// que un mensaje NUNCA se descarte por routing sin binding (evita pérdida silenciosa aunque el worker aún no haya
/// arrancado; el mensaje espera en la cola durable). La no-duplicación se garantiza en el consumidor (inbox
/// idempotente). En nube, el adaptador hermano es Dapr→Service Bus (Épica 8), por el mismo puerto.
/// </summary>
public sealed class PublicadorEventosRabbitMq(string cadenaConexion, ILogger<PublicadorEventosRabbitMq> logger)
    : IPublicadorEventos, IAsyncDisposable
{
    public const string Exchange = "hotelbookinghub.eventos";

    // Cola durable del consumidor de notificaciones (coincide con ConsumidorRabbitMq.Cola). El productor la
    // declara para que el mensaje se retenga aunque el worker no esté suscrito todavía (anti-pérdida silenciosa).
    private const string ColaNotificaciones = "notificaciones.reservas";
    private const string Topico = "reservas";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _conexion;
    private IChannel? _canal;

    public async Task PublicarAsync(string topico, EventoIntegracion evento, CancellationToken ct)
    {
        var cuerpo = JsonSerializer.SerializeToUtf8Bytes(evento, _json);
        var propiedades = new BasicProperties
        {
            Persistent = true,
            MessageId = evento.Id.ToString(),
            Type = evento.Type,
            ContentType = "application/json",
        };

        // La publicación se serializa bajo el mismo gate que la (re)conexión: un IChannel de RabbitMQ.Client 7.x
        // NO es seguro para operaciones concurrentes. Si el broker no está, la excepción se PROPAGA → el
        // ProcesadorOutbox deja la fila Pendiente y la re-reclama al vencer el lease (at-least-once, AC-E9.1.5).
        await _gate.WaitAsync(ct);
        try
        {
            var canal = await ObtenerCanalAsync(ct);
            await canal.BasicPublishAsync(
                exchange: Exchange, routingKey: topico, mandatory: false,
                basicProperties: propiedades, body: cuerpo, cancellationToken: ct);
        }
        finally
        {
            _gate.Release();
        }

        logger.LogInformation(
            "Evento {Tipo} (MessageId {MessageId}) publicado a RabbitMQ [{Exchange}/{Topico}]",
            evento.Type, evento.Id, Exchange, topico);
    }

    /// <summary>Conexión/canal perezosos y reutilizados; reconecta si el canal se cayó, disponiendo los anteriores
    /// (evita fuga de sockets/heartbeats). Llamado siempre bajo <see cref="_gate"/>.</summary>
    private async Task<IChannel> ObtenerCanalAsync(CancellationToken ct)
    {
        if (_canal is { IsOpen: true })
        {
            return _canal;
        }

        await CerrarAsync();

        var fabrica = new ConnectionFactory { Uri = new Uri(cadenaConexion) };
        _conexion = await fabrica.CreateConnectionAsync(ct);
        _canal = await _conexion.CreateChannelAsync(cancellationToken: ct);

        // Topología durable idempotente: exchange + cola del consumidor + binding, para que el mensaje se retenga
        // aunque el consumidor aún no exista (sin pérdida silenciosa por routing sin binding).
        await _canal.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);
        await _canal.QueueDeclareAsync(ColaNotificaciones, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await _canal.QueueBindAsync(ColaNotificaciones, Exchange, Topico, cancellationToken: ct);
        return _canal;
    }

    private async Task CerrarAsync()
    {
        if (_canal is not null)
        {
            await _canal.DisposeAsync();
            _canal = null;
        }

        if (_conexion is not null)
        {
            await _conexion.DisposeAsync();
            _conexion = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CerrarAsync();
        _gate.Dispose();
    }
}
