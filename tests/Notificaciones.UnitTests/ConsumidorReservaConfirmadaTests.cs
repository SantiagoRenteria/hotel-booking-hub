using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Notificaciones.Worker.Notificaciones;

namespace Notificaciones.UnitTests;

/// <summary>
/// Story 5.1a (AC-E5.1a.1) — al consumir <c>ReservaConfirmada.v1</c>, el worker dispara DOS correos (huésped +
/// agente) hacia el sink de Fase 1. Verifica también que el <c>data</c> se deserializa cuando llega como
/// <c>JsonElement</c> (tras el transporte) y que otros tipos de evento se ignoran.
/// </summary>
public sealed class ConsumidorReservaConfirmadaTests
{
    private sealed record CorreoEnviado(string Destinatario, string Asunto, string Cuerpo);

    private sealed class NotificadorFake : INotificador
    {
        public List<CorreoEnviado> Enviados { get; } = [];

        public Task NotificarAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct)
        {
            Enviados.Add(new CorreoEnviado(destinatario, asunto, cuerpo));
            return Task.CompletedTask;
        }
    }

    private static ReservaConfirmadaV1 Data() => new(
        AggregateId: Guid.CreateVersion7(),
        HotelId: Guid.NewGuid(),
        HotelNombre: "Hotel Cartagena Real",
        Ciudad: "Cartagena",
        HabitacionId: Guid.NewGuid(),
        Entrada: new DateOnly(2026, 8, 10),
        Salida: new DateOnly(2026, 8, 14),
        HuespedNombre: "Andrés Pérez",
        HuespedEmail: "andres@example.com",
        AgenteEmail: "carolina@agencia.com",
        PrecioTotal: 1428.00m);

    private static EventoIntegracion Envelope(object data, string tipo = "ReservaConfirmada.v1") => new(
        Id: Guid.CreateVersion7(),
        Type: tipo,
        Version: 1,
        OccurredAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        TraceId: null,
        Data: data);

    [Fact]
    public async Task Confirmacion_dispara_correo_al_huesped_y_al_agente()
    {
        var fake = new NotificadorFake();
        var consumidor = new ConsumidorReservaConfirmada(fake);
        var data = Data();

        await consumidor.ProcesarAsync(Envelope(data), CancellationToken.None);

        Assert.Equal(2, fake.Enviados.Count);
        Assert.Contains(fake.Enviados, c => c.Destinatario == data.HuespedEmail);
        Assert.Contains(fake.Enviados, c => c.Destinatario == data.AgenteEmail);
        // El cuerpo referencia datos de la reserva (hotel).
        Assert.All(fake.Enviados, c => Assert.Contains("Hotel Cartagena Real", c.Cuerpo));
    }

    [Fact]
    public async Task Data_como_JsonElement_se_deserializa_igual()
    {
        var fake = new NotificadorFake();
        var consumidor = new ConsumidorReservaConfirmada(fake);
        // Simula el transporte: data llega como JsonElement, no como el tipo concreto.
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var jsonElement = JsonSerializer.SerializeToElement(Data(), opciones);

        await consumidor.ProcesarAsync(Envelope(jsonElement), CancellationToken.None);

        Assert.Equal(2, fake.Enviados.Count);
        Assert.Contains(fake.Enviados, c => c.Destinatario == "andres@example.com");
    }

    [Fact]
    public async Task Payload_nulo_lanza_error_descriptivo_no_NRE()
    {
        var fake = new NotificadorFake();
        var consumidor = new ConsumidorReservaConfirmada(fake);
        var jsonNulo = JsonSerializer.SerializeToElement<ReservaConfirmadaV1?>(null); // data = "null" tras transporte

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            consumidor.ProcesarAsync(Envelope(jsonNulo), CancellationToken.None));

        Assert.Contains("ReservaConfirmadaV1", ex.Message); // descriptivo, no NullReferenceException
        Assert.Empty(fake.Enviados);
    }

    [Fact]
    public async Task Otro_tipo_de_evento_se_ignora()
    {
        var fake = new NotificadorFake();
        var consumidor = new ConsumidorReservaConfirmada(fake);

        await consumidor.ProcesarAsync(Envelope(Data(), tipo: "OtroEvento.v1"), CancellationToken.None);

        Assert.Empty(fake.Enviados);
    }
}
