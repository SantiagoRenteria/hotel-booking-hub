using System.Diagnostics;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Mensajeria.Behaviors;
using HotelBookingHub.Comun.Observabilidad;

namespace Comun.Web.UnitTests.Observabilidad;

/// <summary>
/// Story 7.1 (AC-E7.1.2 / AC-E7.1.4) — el pipeline del mediator emite UN span de negocio desde el
/// <c>ActivitySource</c> compartido "HotelBookingHub", y un fallo del handler marca ese span como Error.
/// Verificación determinista con un <c>ActivityListener</c> en memoria (no depende del dashboard de Aspire).
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

    [Fact]
    public async Task Emite_un_span_desde_el_source_compartido_con_el_nombre_de_la_peticion()
    {
        // Given: un listener sobre el source "HotelBookingHub" y el behavior de tracing.
        var capturadas = new List<Activity>();
        using var listener = EscucharSourceCompartido(capturadas);
        var behavior = new TracingBehavior<PeticionFake, string>();

        // When: la petición atraviesa el behavior con éxito.
        var respuesta = await behavior.Handle(new PeticionFake(), _ => Task.FromResult("ok"), CancellationToken.None);

        // Then: se emitió exactamente 1 span, desde el source compartido, nombrado como la petición.
        Assert.Equal("ok", respuesta);
        var span = Assert.Single(capturadas);
        Assert.Equal(ActividadHotelBookingHub.NombreSource, span.Source.Name);
        Assert.Equal(nameof(PeticionFake), span.DisplayName);
    }

    [Fact]
    public async Task Marca_el_span_como_Error_cuando_el_handler_lanza()
    {
        // Given: un listener y el behavior.
        var capturadas = new List<Activity>();
        using var listener = EscucharSourceCompartido(capturadas);
        var behavior = new TracingBehavior<PeticionFake, string>();

        // When: el siguiente eslabón del pipeline falla.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new PeticionFake(), _ => throw new InvalidOperationException("boom"), CancellationToken.None));

        // Then: el span quedó marcado como Error (el operador ve el span exacto del fallo).
        var span = Assert.Single(capturadas);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }
}
