using HotelBookingHub.Comun.Eventos;
using Notificaciones.Worker.Notificaciones;

namespace Notificaciones.UnitTests;

/// <summary>
/// Story 5.1b (Task 4) — mensaje-veneno: un evento que falla SIEMPRE al procesar no debe re-reclamarse sin cota
/// ni bloquear el stream. Tras el tope de intentos se aparta a dead-letter y se hace ACK (no se relanza).
/// </summary>
public sealed class DespachadorNotificacionesTests
{
    /// <summary>Procesador que falla las primeras <c>fallosIniciales</c> veces (int.MaxValue = veneno permanente).</summary>
    private sealed class ProcesadorFake(int fallosIniciales) : IProcesadorEvento
    {
        public int Invocaciones { get; private set; }

        public Task ProcesarAsync(EventoIntegracion evento, CancellationToken ct)
        {
            Invocaciones++;
            if (Invocaciones <= fallosIniciales)
            {
                throw new InvalidOperationException("Fallo de procesamiento simulado (veneno).");
            }

            return Task.CompletedTask;
        }
    }

    private sealed record Apartado(EventoIntegracion Evento, string Motivo, int Intentos);

    private sealed class DeadLetterFake : IColaDeadLetter
    {
        public List<Apartado> Apartados { get; } = [];

        public Task EnviarAsync(EventoIntegracion evento, string motivo, int intentos, CancellationToken ct)
        {
            Apartados.Add(new Apartado(evento, motivo, intentos));
            return Task.CompletedTask;
        }
    }

    private static EventoIntegracion Evento() => new(
        Id: Guid.CreateVersion7(),
        Type: ReservaConfirmadaV1.Tipo,
        Version: 1,
        OccurredAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        TraceId: null,
        Data: new object());

    private static DespachadorNotificaciones Crear(
        IProcesadorEvento procesador, IColaDeadLetter deadLetter, int maxIntentos) =>
        new(procesador, new ContadorReintentosEnMemoria(), deadLetter, new OpcionesDespachador(maxIntentos));

    [Fact]
    public async Task Mensaje_veneno_va_a_dead_letter_tras_el_tope_de_intentos()
    {
        // Given: un procesador que SIEMPRE falla y un tope de 3 intentos.
        var veneno = new ProcesadorFake(fallosIniciales: int.MaxValue);
        var deadLetter = new DeadLetterFake();
        var despachador = Crear(veneno, deadLetter, maxIntentos: 3);
        var evento = Evento();

        // When: el broker re-entrega hasta agotar el tope. Los primeros intentos relanzan (para reentrega);
        // el que alcanza el tope hace ACK (no relanza) y aparta a dead-letter.
        await Assert.ThrowsAsync<InvalidOperationException>(() => despachador.DespacharAsync(evento, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => despachador.DespacharAsync(evento, CancellationToken.None));
        await despachador.DespacharAsync(evento, CancellationToken.None); // 3er intento: NO relanza.

        // Then: se apartó exactamente 1 vez, con el nº de intentos agotados.
        Assert.Single(deadLetter.Apartados);
        Assert.Equal(3, deadLetter.Apartados[0].Intentos);
        Assert.Equal(evento.Id, deadLetter.Apartados[0].Evento.Id);
    }

    [Fact]
    public async Task Antes_del_tope_relanza_para_permitir_la_reentrega()
    {
        // Given: veneno permanente, tope 3.
        var veneno = new ProcesadorFake(fallosIniciales: int.MaxValue);
        var deadLetter = new DeadLetterFake();
        var despachador = Crear(veneno, deadLetter, maxIntentos: 3);
        var evento = Evento();

        // When/Then: los intentos previos al tope propagan (para que el transporte re-entregue) y NO apartan aún.
        await Assert.ThrowsAsync<InvalidOperationException>(() => despachador.DespacharAsync(evento, CancellationToken.None));
        Assert.Empty(deadLetter.Apartados);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void F3_MaxIntentos_invalido_es_rechazado(int maxIntentos)
    {
        // Un tope < 1 degrada la garantía (dead-letter al primer fallo o reintento infinito): debe fallar temprano.
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpcionesDespachador(maxIntentos));
    }

    [Fact]
    public async Task Fallo_transitorio_se_recupera_sin_dead_letter()
    {
        // Given: falla 1 vez y luego procesa bien; tope 3.
        var procesador = new ProcesadorFake(fallosIniciales: 1);
        var deadLetter = new DeadLetterFake();
        var despachador = Crear(procesador, deadLetter, maxIntentos: 3);
        var evento = Evento();

        // When: 1ª entrega falla (relanza); la re-entrega tiene éxito.
        await Assert.ThrowsAsync<InvalidOperationException>(() => despachador.DespacharAsync(evento, CancellationToken.None));
        await despachador.DespacharAsync(evento, CancellationToken.None);

        // Then: nunca se apartó a dead-letter.
        Assert.Empty(deadLetter.Apartados);
    }
}
