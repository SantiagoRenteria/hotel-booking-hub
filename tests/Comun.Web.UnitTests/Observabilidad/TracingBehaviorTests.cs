using System.Diagnostics;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Mensajeria.Behaviors;
using HotelBookingHub.Comun.Observabilidad;

namespace Comun.Web.UnitTests.Observabilidad;

/// <summary>
/// Story 7.1 (AC-E7.1.2 / AC-E7.1.4) — el pipeline del mediator emite UN span de negocio desde el
/// <c>ActivitySource</c> compartido "HotelBookingHub", y un fallo del handler marca ese span como Error
/// registrando la excepción (tipo + mensaje). Verificación determinista con un <c>ActivityListener</c> en
/// memoria (no depende del dashboard de Aspire). El listener es process-wide: se filtra por <c>DisplayName</c>
/// para no capturar spans de otras clases de test que corran en paralelo sobre el mismo source.
/// </summary>
public sealed class TracingBehaviorTests
{
    private sealed record PeticionFake : IRequest<string>;

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

    private static Activity SpanDe(List<Activity> capturadas) =>
        Assert.Single(capturadas, s => s.DisplayName == nameof(PeticionFake));

    [Fact]
    public async Task Emite_un_span_desde_el_source_compartido_con_el_nombre_de_la_peticion()
    {
        // Given: un listener sobre el source "HotelBookingHub" y el behavior de tracing.
        var capturadas = new List<Activity>();
        using var listener = EscucharSourceCompartido(capturadas);
        var behavior = new TracingBehavior<PeticionFake, string>();

        // When: la petición atraviesa el behavior con éxito.
        var respuesta = await behavior.Handle(new PeticionFake(), _ => Task.FromResult("ok"), CancellationToken.None);

        // Then: se emitió el span de la petición, desde el source compartido.
        Assert.Equal("ok", respuesta);
        var span = SpanDe(capturadas);
        Assert.Equal(ActividadHotelBookingHub.NombreSource, span.Source.Name);
    }

    [Fact]
    public async Task Marca_el_span_Error_y_registra_la_excepcion_cuando_el_handler_lanza()
    {
        // Given: un listener y el behavior.
        var capturadas = new List<Activity>();
        using var listener = EscucharSourceCompartido(capturadas);
        var behavior = new TracingBehavior<PeticionFake, string>();

        // When: el siguiente eslabón del pipeline falla.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new PeticionFake(), _ => throw new InvalidOperationException("boom"), CancellationToken.None));

        // Then: el span quedó Error CON la excepción registrada (tipo + mensaje) → el operador ve el span exacto.
        var span = SpanDe(capturadas);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("boom", span.StatusDescription);
        var eventoExcepcion = Assert.Single(span.Events, e => e.Name == "exception");
        Assert.Contains(eventoExcepcion.Tags, t =>
            t.Key == "exception.type" && (string?)t.Value == typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public async Task Una_cancelacion_no_marca_el_span_como_Error()
    {
        // Given: un listener y el behavior.
        var capturadas = new List<Activity>();
        using var listener = EscucharSourceCompartido(capturadas);
        var behavior = new TracingBehavior<PeticionFake, string>();

        // When: el pipeline se cancela (aborto de cliente / shutdown), no un fallo del servidor.
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            behavior.Handle(new PeticionFake(), _ => throw new OperationCanceledException(), CancellationToken.None));

        // Then: el span NO se marca Error (la cancelación no es un fallo).
        var span = SpanDe(capturadas);
        Assert.NotEqual(ActivityStatusCode.Error, span.Status);
    }
}
