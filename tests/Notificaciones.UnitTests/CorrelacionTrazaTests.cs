using System.Diagnostics;
using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Observabilidad;
using Notificaciones.Worker.Notificaciones;

namespace Notificaciones.UnitTests;

/// <summary>
/// Story 7.1 (AC-E7.1.3 / AC-E7.1.4) — el span del consumidor del worker se correlaciona con la traza originante
/// mediante el <c>trace-id</c> de negocio del envelope, y un fallo del procesamiento marca ese span como Error
/// con la excepción registrada. Verificación determinista con <c>ActivityListener</c>. El listener es
/// process-wide; cada test filtra por un discriminador único (trace-id o <c>DisplayName</c> con un <c>Type</c>
/// propio) para no capturar spans de otras clases de test que corran en paralelo sobre el mismo source.
/// </summary>
public sealed class CorrelacionTrazaTests
{
    private sealed class ProcesadorOk : IProcesadorEvento
    {
        public Task ProcesarAsync(EventoIntegracion evento, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ProcesadorQueFalla : IProcesadorEvento
    {
        public Task ProcesarAsync(EventoIntegracion evento, CancellationToken ct) =>
            throw new InvalidOperationException("fallo de procesamiento");
    }

    private sealed class DeadLetterNoop : IColaDeadLetter
    {
        public Task EnviarAsync(EventoIntegracion evento, string motivo, int intentos, CancellationToken ct) => Task.CompletedTask;
    }

    private static ActivityListener EscucharSourceCompartido(List<Activity> capturadas)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = fuente => fuente.Name == ActividadHotelBookingHub.NombreSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = capturadas.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static EventoIntegracion Evento(string tipo, string? traceId) => new(
        Id: Guid.CreateVersion7(),
        Type: tipo,
        Version: 1,
        OccurredAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        TraceId: traceId,
        Data: new object());

    private static DespachadorNotificaciones Crear(IProcesadorEvento procesador) =>
        new(procesador, new ContadorReintentosEnMemoria(), new DeadLetterNoop(), new OpcionesDespachador(3));

    [Fact]
    public async Task El_span_del_consumidor_cuelga_de_la_misma_traza_que_el_trace_id_de_negocio()
    {
        // Given: un evento cuyo envelope lleva un trace-id de negocio (32 hex, formato de Activity.TraceId).
        var capturadas = new List<Activity>();
        using var listener = EscucharSourceCompartido(capturadas);
        const string traceId = "0af7651916cd43dd8448eb211c80319c";

        // When: el worker despacha el evento.
        await Crear(new ProcesadorOk()).DespacharAsync(Evento("PruebaTraza32.v1", traceId), CancellationToken.None);

        // Then: el span del consumidor comparte el MISMO trace-id (misma traza en el dashboard) y es de tipo Consumer.
        var span = Assert.Single(capturadas, s => s.TraceId.ToString() == traceId);
        Assert.Equal(ActivityKind.Consumer, span.Kind);
    }

    [Fact]
    public async Task Acepta_un_traceparent_W3C_completo_y_conserva_el_trace_id()
    {
        // Given: algunos productores/contratos transportan el traceparent W3C completo.
        var capturadas = new List<Activity>();
        using var listener = EscucharSourceCompartido(capturadas);
        const string traceparent = "00-1bf7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
        const string traceIdEmbebido = "1bf7651916cd43dd8448eb211c80319c";

        // When: el worker despacha el evento.
        await Crear(new ProcesadorOk()).DespacharAsync(Evento("PruebaTrazaParent.v1", traceparent), CancellationToken.None);

        // Then: el span cuelga del trace-id embebido en el traceparent.
        Assert.Single(capturadas, s => s.TraceId.ToString() == traceIdEmbebido);
    }

    [Fact]
    public async Task Sin_trace_id_arranca_una_traza_raiz_sin_fallar()
    {
        // Given: un evento sin trace-id (p. ej. emitido fuera de una actividad).
        var capturadas = new List<Activity>();
        using var listener = EscucharSourceCompartido(capturadas);

        // When: el worker despacha el evento.
        await Crear(new ProcesadorOk()).DespacharAsync(Evento("PruebaTrazaRaiz.v1", traceId: null), CancellationToken.None);

        // Then: no revienta; se emite un span de consumidor (traza raíz propia).
        var span = Assert.Single(capturadas, s => s.DisplayName == "consumir PruebaTrazaRaiz.v1");
        Assert.Equal(ActivityKind.Consumer, span.Kind);
    }

    [Fact]
    public async Task Un_fallo_del_procesamiento_marca_el_span_del_consumidor_Error_con_la_excepcion()
    {
        // Given: un procesador que falla y un evento con trace-id de negocio.
        var capturadas = new List<Activity>();
        using var listener = EscucharSourceCompartido(capturadas);
        const string traceId = "2cf7651916cd43dd8448eb211c80319c";

        // When: el worker despacha el evento (el fallo se propaga para re-entrega; antes del tope no va a dead-letter).
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Crear(new ProcesadorQueFalla()).DespacharAsync(Evento("PruebaTrazaFallo.v1", traceId), CancellationToken.None));

        // Then: el span del consumidor quedó Error CON la excepción registrada (tipo + mensaje) — AC-E7.1.4 en el salto asíncrono.
        var span = Assert.Single(capturadas, s => s.TraceId.ToString() == traceId);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        var eventoExcepcion = Assert.Single(span.Events, e => e.Name == "exception");
        Assert.Contains(eventoExcepcion.Tags, t =>
            t.Key == "exception.type" && (string?)t.Value == typeof(InvalidOperationException).FullName);
    }
}
