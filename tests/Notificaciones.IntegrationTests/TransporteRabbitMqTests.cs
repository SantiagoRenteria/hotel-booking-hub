using System.Collections.Concurrent;
using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Microsoft.Extensions.Logging.Abstractions;
using Notificaciones.Worker.Notificaciones;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Notificaciones.IntegrationTests;

/// <summary>
/// Story 9.1 (AC-E9.1.2/.3) — transporte real de eventos por RabbitMQ, end-to-end: un evento publicado en el
/// broker es consumido por <see cref="ConsumidorRabbitMq"/>, enrutado por tipo (<see cref="EnrutadorNotificaciones"/>)
/// y disparado como notificación EXACTAMENTE 1 vez; una re-entrega del mismo <c>MessageId</c> NO re-emite (dedup por
/// el inbox idempotente). Determinista con Testcontainers RabbitMQ dentro de <c>dotnet test</c> (no smoke manual).
/// </summary>
[Collection("rabbitmq-transporte")]
public sealed class TransporteRabbitMqTests : IAsyncLifetime
{
    // Contrato de transporte: debe coincidir con PublicadorEventosRabbitMq.Exchange y con el binding del consumidor.
    private const string Exchange = "hotelbookinghub.eventos";
    private const string Cola = "notificaciones.reservas";
    private const string Topico = "reservas";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4.0-management-alpine").Build();

    public Task InitializeAsync() => _rabbit.StartAsync();
    public Task DisposeAsync() => _rabbit.DisposeAsync().AsTask();

    private sealed class NotificadorFake : INotificador
    {
        public ConcurrentBag<string> Destinatarios { get; } = [];

        public Task NotificarAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct)
        {
            Destinatarios.Add(destinatario);
            return Task.CompletedTask;
        }
    }

    private static EventoIntegracion EventoConfirmada(Guid messageId) => new(
        Id: messageId,
        Type: ReservaConfirmadaV1.Tipo,
        Version: 1,
        OccurredAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        TraceId: null,
        Data: new ReservaConfirmadaV1(
            AggregateId: Guid.CreateVersion7(), HotelId: Guid.CreateVersion7(), HotelNombre: "Hotel Central",
            Ciudad: "Bogotá", HabitacionId: Guid.CreateVersion7(), Entrada: new DateOnly(2026, 8, 1),
            Salida: new DateOnly(2026, 8, 3), HuespedNombre: "Ana", HuespedEmail: "ana@x.com",
            AgenteEmail: "agente@x.com", PrecioTotal: 200m));

    private async Task PublicarAsync(EventoIntegracion evento)
    {
        var fabrica = new ConnectionFactory { Uri = new Uri(_rabbit.GetConnectionString()) };
        await using var conexion = await fabrica.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync();
        // Topología durable declarada antes de publicar (idempotente): el mensaje queda en la cola aunque el
        // consumidor aún no esté suscrito → sin carrera de arranque; el consumidor la drena cuando conecta.
        await canal.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, autoDelete: false);
        await canal.QueueDeclareAsync(Cola, durable: true, exclusive: false, autoDelete: false);
        await canal.QueueBindAsync(Cola, Exchange, Topico);
        var cuerpo = JsonSerializer.SerializeToUtf8Bytes(evento, _json);
        var propiedades = new BasicProperties { Persistent = true, MessageId = evento.Id.ToString(), Type = evento.Type };
        await canal.BasicPublishAsync(Exchange, Topico, mandatory: false, basicProperties: propiedades, body: cuerpo);
    }

    private static EnrutadorNotificaciones Enrutador(INotificador notificador)
    {
        var inbox = new InboxIdempotenciaEnMemoria();
        return new EnrutadorNotificaciones(
            new ConsumidorReservaConfirmada(notificador, inbox),
            new ConsumidorSolicitudCancelacion(notificador, inbox),
            new ConsumidorResolucionCancelacion(notificador, inbox),
            new ContadorReintentosEnMemoria(),
            new ColaDeadLetterLog(NullLogger<ColaDeadLetterLog>.Instance),
            new OpcionesDespachador(5));
    }

    private static async Task<bool> EsperarHasta(Func<bool> condicion, TimeSpan timeout)
    {
        var limite = timeout;
        for (var i = 0; i < 150 && limite > TimeSpan.Zero; i++)
        {
            if (condicion())
            {
                return true;
            }

            await Task.Delay(100);
            limite -= TimeSpan.FromMilliseconds(100);
        }

        return condicion();
    }

    [Fact]
    public async Task Evento_publicado_en_rabbit_dispara_la_notificacion_exactamente_una_vez()
    {
        // Given: el worker consumidor conectado al broker real.
        var notificador = new NotificadorFake();
        var consumidor = new ConsumidorRabbitMq(_rabbit.GetConnectionString(), Enrutador(notificador), NullLogger<ConsumidorRabbitMq>.Instance);
        await consumidor.StartAsync(CancellationToken.None);
        var evento = EventoConfirmada(Guid.CreateVersion7());

        // When: se publica una ReservaConfirmada.v1 en el topic "reservas".
        await PublicarAsync(evento);

        // Then: el worker la consume y dispara 2 correos (huésped + agente).
        var llego = await EsperarHasta(() => notificador.Destinatarios.Count >= 2, TimeSpan.FromSeconds(15));
        Assert.True(llego, $"No llegó la notificación; destinatarios vistos: {notificador.Destinatarios.Count}");
        Assert.Equal(2, notificador.Destinatarios.Count);

        // When: se RE-ENTREGA el mismo MessageId (duplicado del broker / reintento).
        await PublicarAsync(evento);
        await Task.Delay(TimeSpan.FromSeconds(2)); // margen para que un consumo indebido se manifieste

        // Then: NO se re-emite (dedup por el inbox idempotente) → siguen 2.
        Assert.Equal(2, notificador.Destinatarios.Count);

        await consumidor.StopAsync(CancellationToken.None);
    }
}
