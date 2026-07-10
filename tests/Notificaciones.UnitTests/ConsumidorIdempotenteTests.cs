using HotelBookingHub.Comun.Eventos;
using Notificaciones.Worker.Notificaciones;

namespace Notificaciones.UnitTests;

/// <summary>
/// Story 5.1b (AC-E5.1b.1) — idempotencia del CONSUMIDOR. Con el mismo evento entregado N veces (at-least-once),
/// el worker envía <b>exactamente 1</b> correo por destinatario (dedup por <c>(MessageId, version, destinatario)</c>
/// vía el inbox <c>SETNX</c>). Escenarios en Given/When/Then; el efecto se cuenta por destinatario.
/// </summary>
public sealed class ConsumidorIdempotenteTests
{
    private sealed record CorreoEnviado(string Destinatario, string Asunto, string Cuerpo);

    /// <summary>Notificador de prueba: cuenta envíos y puede fallar de forma programada las primeras veces que
    /// se le pide un destinatario concreto (simula un fallo transitorio del adaptador SMTP).</summary>
    private sealed class NotificadorFake : INotificador
    {
        private readonly Dictionary<string, int> _fallosPendientes;

        public NotificadorFake(Dictionary<string, int>? fallosPendientes = null) =>
            _fallosPendientes = fallosPendientes ?? [];

        public List<CorreoEnviado> Enviados { get; } = [];

        public Task NotificarAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct)
        {
            if (_fallosPendientes.TryGetValue(destinatario, out var restantes) && restantes > 0)
            {
                _fallosPendientes[destinatario] = restantes - 1;
                throw new InvalidOperationException($"Fallo transitorio simulado enviando a {destinatario}.");
            }

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

    private static EventoIntegracion Envelope(object data, Guid? id = null, int version = 1) => new(
        Id: id ?? Guid.CreateVersion7(),
        Type: ReservaConfirmadaV1.Tipo,
        Version: version,
        OccurredAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        TraceId: null,
        Data: data);

    [Fact]
    public async Task Mismo_evento_entregado_N_veces_envia_exactamente_un_correo_por_destinatario()
    {
        // Given: un evento y un worker con inbox de idempotencia.
        var fake = new NotificadorFake();
        var consumidor = new ConsumidorReservaConfirmada(fake, new InboxIdempotenciaEnMemoria());
        var data = Data();
        var evento = Envelope(data);

        // When: el broker lo entrega 5 veces (at-least-once).
        for (var i = 0; i < 5; i++)
        {
            await consumidor.ProcesarAsync(evento, CancellationToken.None);
        }

        // Then: exactamente 1 correo por destinatario (efecto == 1), no 5.
        Assert.Equal(2, fake.Enviados.Count);
        Assert.Equal(1, fake.Enviados.Count(c => c.Destinatario == data.HuespedEmail));
        Assert.Equal(1, fake.Enviados.Count(c => c.Destinatario == data.AgenteEmail));
    }

    [Fact]
    public async Task Entregas_concurrentes_del_mismo_evento_no_duplican()
    {
        // Given: el mismo evento a punto de entregarse en paralelo (competing consumers / reintentos solapados).
        var fake = new NotificadorFake();
        var consumidor = new ConsumidorReservaConfirmada(fake, new InboxIdempotenciaEnMemoria());
        var evento = Envelope(Data());

        // When: 10 entregas concurrentes.
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            consumidor.ProcesarAsync(evento, CancellationToken.None)));

        // Then: la reserva atómica garantiza un solo efecto por destinatario.
        Assert.Equal(2, fake.Enviados.Count);
    }

    [Fact]
    public async Task Fallo_del_segundo_correo_no_duplica_el_primero_al_reintentar()
    {
        // Given: el envío al AGENTE falla la primera vez; el del huésped sale bien.
        var fake = new NotificadorFake(new Dictionary<string, int> { ["carolina@agencia.com"] = 1 });
        var consumidor = new ConsumidorReservaConfirmada(fake, new InboxIdempotenciaEnMemoria());
        var evento = Envelope(Data());

        // When: 1ª entrega → el 2º correo falla y propaga (para que el broker reintegre).
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            consumidor.ProcesarAsync(evento, CancellationToken.None));

        // And: el broker re-entrega el MISMO evento.
        await consumidor.ProcesarAsync(evento, CancellationToken.None);

        // Then: el huésped recibió 1 (no se re-envió: su reserva quedó tras el éxito) y el agente recibió 1
        // (su reserva se liberó al fallar y el reintento lo completó). Sin duplicado, sin pérdida.
        Assert.Equal(1, fake.Enviados.Count(c => c.Destinatario == "andres@example.com"));
        Assert.Equal(1, fake.Enviados.Count(c => c.Destinatario == "carolina@agencia.com"));
    }

    [Fact]
    public async Task Eventos_distintos_no_se_deduplican_entre_si()
    {
        // Given: dos confirmaciones DISTINTAS (distinto MessageId) para el mismo huésped/agente.
        var fake = new NotificadorFake();
        var consumidor = new ConsumidorReservaConfirmada(fake, new InboxIdempotenciaEnMemoria());

        // When: se procesan ambas.
        await consumidor.ProcesarAsync(Envelope(Data(), id: Guid.CreateVersion7()), CancellationToken.None);
        await consumidor.ProcesarAsync(Envelope(Data(), id: Guid.CreateVersion7()), CancellationToken.None);

        // Then: 4 correos (2 por evento): la dedup es por mensaje, no colapsa eventos distintos.
        Assert.Equal(4, fake.Enviados.Count);
    }
}
