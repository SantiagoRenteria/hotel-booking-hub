using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Hoteles.Infrastructure.Mensajeria;

/// <summary>
/// Adaptador de transporte REAL de <see cref="IPublicadorEventos"/> por RabbitMQ (Story 9.1, ADR-019 — entorno
/// local). Publica el envelope <see cref="EventoIntegracion"/> serializado a JSON en un exchange direct durable,
/// con routing key = <c>topico</c> ("hoteles"). Mensaje persistente. La no-duplicación se garantiza en el
/// consumidor (inbox idempotente), no aquí. En nube, el adaptador hermano es Dapr→Service Bus (Épica 8).
/// (Duplicación deliberada del adaptador de Reservas: sin acoplamiento oculto entre BCs, regla del repo.)
/// </summary>
public sealed class PublicadorEventosRabbitMq(string cadenaConexion, ILogger<PublicadorEventosRabbitMq> logger)
    : IPublicadorEventos, IAsyncDisposable
{
    public const string Exchange = "hotelbookinghub.eventos";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _conexion;
    private IChannel? _canal;

    public async Task PublicarAsync(string topico, EventoIntegracion evento, CancellationToken ct)
    {
        // Excepción PROPAGADA si el broker no está: el ProcesadorOutbox deja la fila Pendiente (at-least-once).
        var canal = await ObtenerCanalAsync(ct);

        var cuerpo = JsonSerializer.SerializeToUtf8Bytes(evento, _json);
        var propiedades = new BasicProperties
        {
            Persistent = true,
            MessageId = evento.Id.ToString(),
            Type = evento.Type,
            ContentType = "application/json",
        };

        await canal.BasicPublishAsync(
            exchange: Exchange, routingKey: topico, mandatory: false,
            basicProperties: propiedades, body: cuerpo, cancellationToken: ct);

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

        await _gate.WaitAsync(ct);
        try
        {
            if (_canal is { IsOpen: true })
            {
                return _canal;
            }

            var fabrica = new ConnectionFactory { Uri = new Uri(cadenaConexion) };
            _conexion = await fabrica.CreateConnectionAsync(ct);
            _canal = await _conexion.CreateChannelAsync(cancellationToken: ct);
            await _canal.ExchangeDeclareAsync(
                Exchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);
            return _canal;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_canal is not null)
        {
            await _canal.DisposeAsync();
        }

        if (_conexion is not null)
        {
            await _conexion.DisposeAsync();
        }

        _gate.Dispose();
    }
}
