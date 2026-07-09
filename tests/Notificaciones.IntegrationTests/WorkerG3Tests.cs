using System.Collections.Concurrent;
using HotelBookingHub.Comun.Eventos;
using Notificaciones.Worker.Notificaciones;
using Xunit;

namespace Notificaciones.IntegrationTests;

/// <summary>
/// Story 5.1b — supervivencia a la caída del broker (G3, AC-E5.1b.2) e idempotencia bajo re-entrega
/// (AC-E5.1b.1), con el inbox Redis REAL. Enciende el assert del lado CONSUMIDOR que E1 dejó como deuda
/// (<c>AC-E1.6b.4</c>): tras una ráfaga con reentregas y "broker caído→recuperado", 100% entregado, 0 perdidos,
/// 0 duplicados.
/// </summary>
[Collection("Redis")]
public sealed class WorkerG3Tests(RedisFixture fixture)
{
    /// <summary>Notificador thread-safe: cuenta envíos por destinatario y puede fallar de forma programada las
    /// primeras N veces por destinatario (simula la interrupción del envío al caer el broker/worker).</summary>
    private sealed class NotificadorFake : INotificador
    {
        private readonly object _cerrojo = new();
        private readonly ConcurrentDictionary<string, int> _fallosPendientes;

        public NotificadorFake(IDictionary<string, int>? fallosPendientes = null) =>
            _fallosPendientes = new ConcurrentDictionary<string, int>(fallosPendientes ?? new Dictionary<string, int>());

        public ConcurrentBag<string> Enviados { get; } = [];

        public Task NotificarAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct)
        {
            lock (_cerrojo)
            {
                if (_fallosPendientes.TryGetValue(destinatario, out var restantes) && restantes > 0)
                {
                    _fallosPendientes[destinatario] = restantes - 1;
                    throw new InvalidOperationException($"Envío interrumpido (broker caído) a {destinatario}.");
                }

                Enviados.Add(destinatario);
            }

            return Task.CompletedTask;
        }
    }

    private static ReservaConfirmadaV1 Data(int i) => new(
        AggregateId: Guid.CreateVersion7(),
        HotelId: Guid.NewGuid(),
        HotelNombre: $"Hotel {i}",
        Ciudad: "Cartagena",
        HabitacionId: Guid.NewGuid(),
        Entrada: new DateOnly(2026, 8, 10),
        Salida: new DateOnly(2026, 8, 14),
        HuespedNombre: $"Huésped {i}",
        HuespedEmail: $"huesped-{i}@example.com",
        AgenteEmail: $"agente-{i}@example.com",
        PrecioTotal: 1000m + i);

    private static EventoIntegracion Envelope(ReservaConfirmadaV1 data) => new(
        Id: Guid.CreateVersion7(),
        Type: ReservaConfirmadaV1.Tipo,
        Version: 1,
        OccurredAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        TraceId: null,
        Data: data);

    private ConsumidorReservaConfirmada Crear(INotificador notificador) =>
        new(notificador, new InboxIdempotenciaRedis(fixture.Redis, new OpcionesInbox(TimeSpan.FromMinutes(10))));

    [Fact]
    public async Task Rafaga_con_reentregas_multiples_entrega_exactamente_una_vez_por_destinatario()
    {
        // Given: una ráfaga de 20 eventos distintos, cada uno entregado 3 veces (at-least-once).
        const int n = 20, entregasPorEvento = 3;
        var fake = new NotificadorFake();
        var consumidor = Crear(fake);
        var eventos = Enumerable.Range(0, n).Select(i => Envelope(Data(i))).ToList();

        // When: TODAS las entregas (n * 3) se procesan en paralelo (reentregas solapadas del broker).
        var entregas = eventos
            .SelectMany(e => Enumerable.Repeat(e, entregasPorEvento))
            .Select(e => consumidor.ProcesarAsync(e, CancellationToken.None));
        await Task.WhenAll(entregas);

        // Then: exactamente 2 correos por evento (huésped + agente), sin duplicados: 2n en total.
        Assert.Equal(2 * n, fake.Enviados.Count);
        Assert.Equal(2 * n, fake.Enviados.Distinct().Count()); // cada destinatario único aparece 1 sola vez.
    }

    [Fact]
    public async Task Broker_caido_durante_la_rafaga_no_pierde_ni_duplica_al_recuperarse()
    {
        // Given: 15 eventos. Mientras el broker está caído, TODO envío falla: se interrumpe el 1er envío de cada
        // destinatario (huésped y agente). El consumidor intenta ambos efectos, libera las reservas y propaga para
        // que el transporte re-entregue al recuperarse. Modela "todo envío falla durante la caída".
        const int n = 15;
        var eventos = Enumerable.Range(0, n).Select(i => Envelope(Data(i))).ToList();
        var fallos = new Dictionary<string, int>();
        foreach (var i in Enumerable.Range(0, n))
        {
            fallos[$"huesped-{i}@example.com"] = 1;
            fallos[$"agente-{i}@example.com"] = 1;
        }

        var fake = new NotificadorFake(fallos);
        var consumidor = Crear(fake);

        // When (broker caído): la 1ª entrega de cada evento falla en ambos efectos y propaga (AggregateException);
        // las reservas se liberan (sin pérdida).
        foreach (var e in eventos)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => consumidor.ProcesarAsync(e, CancellationToken.None));
        }

        Assert.Empty(fake.Enviados); // nada salió mientras el broker estuvo caído.

        // When (broker recuperado): re-entrega de todos los pendientes.
        foreach (var e in eventos)
        {
            await consumidor.ProcesarAsync(e, CancellationToken.None);
        }

        // Then: 100% entregado (2n correos), 0 perdidos, 0 duplicados.
        Assert.Equal(2 * n, fake.Enviados.Count);
        Assert.Equal(2 * n, fake.Enviados.Distinct().Count());
    }
}
