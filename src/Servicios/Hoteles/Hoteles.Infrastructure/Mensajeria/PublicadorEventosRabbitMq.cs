using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Hoteles.Infrastructure.Mensajeria;

/// <summary>
/// Adaptador de transporte REAL de <see cref="IPublicadorEventos"/> por RabbitMQ (Story 9.1, ADR-019 — entorno
/// local). Publica el envelope <see cref="EventoIntegracion"/> serializado a JSON en un exchange direct durable,
/// con routing key = <c>topico</c> ("hoteles"). Declara una cola durable de retención + binding para que los
/// eventos de catálogo NO se descarten por routing sin binding (el consumidor real — la proyección de Reservas —
/// se cablea en 9.2; hasta entonces los eventos esperan en la cola durable, sin pérdida silenciosa). La
/// no-duplicación se garantiza en el consumidor (idempotencia). Duplicación deliberada del adaptador de Reservas.
/// </summary>
public sealed class PublicadorEventosRabbitMq(string cadenaConexion, ILogger<PublicadorEventosRabbitMq> logger)
    : IPublicadorEventos, IAsyncDisposable
{
    public const string Exchange = "hotelbookinghub.eventos";

    // Cola durable de retención de eventos de catálogo (el consumidor de proyección de Reservas llega en 9.2).
    private const string ColaProyeccion = "proyeccion.hoteles";
    private const string Topico = "hoteles";

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

        // Publicación serializada bajo el gate (IChannel 7.x no es concurrente). Excepción PROPAGADA si el broker
        // no está: el ProcesadorOutbox deja la fila Pendiente (at-least-once).
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

        await _canal.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);
        await _canal.QueueDeclareAsync(ColaProyeccion, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await _canal.QueueBindAsync(ColaProyeccion, Exchange, Topico, cancellationToken: ct);
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
