using System.Diagnostics;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Observabilidad;
using Hoteles.Application.Abstracciones;
using Hoteles.Application.Hoteles.CrearHotel;
using Hoteles.Domain.Hoteles;
using Hoteles.Domain.Puertos;
using Microsoft.Extensions.DependencyInjection;

namespace Hoteles.UnitTests.Observabilidad;

/// <summary>
/// Story 7.1 (AC-E7.1.2) — el pipeline REAL registrado por <c>AddMediatorPipeline</c> emite un span de negocio
/// desde el source compartido "HotelBookingHub" al despachar un comando. Prueba el cableado del
/// <c>TracingBehavior</c> en <c>RegistroMediador</c> de punta a punta (no solo el behavior aislado).
/// </summary>
public sealed class TrazaPipelineTests
{
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

    private static ServiceProvider Construir()
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddSingleton<IHotelRepository>(new HotelRepositoryFake());
        servicios.AddSingleton<IContextoAgente>(new ContextoAgenteFake());
        servicios.AddMediatorPipeline(typeof(CrearHotelCommand).Assembly);
        return servicios.BuildServiceProvider();
    }

    [Fact]
    public async Task El_pipeline_real_emite_un_span_de_negocio_desde_el_source_compartido()
    {
        // Given: el pipeline del mediator cableado como en producción + un listener sobre el source compartido.
        var capturadas = new List<Activity>();
        using var listener = EscucharSourceCompartido(capturadas);
        using var proveedor = Construir();
        using var scope = proveedor.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        // When: se despacha un comando válido.
        var resultado = await sender.Send(
            new CrearHotelCommand("Hotel Central", "Medellín", "Calle 1", "desc", EstadoHotel.Habilitado),
            CancellationToken.None);

        // Then: hubo éxito y se emitió un span desde "HotelBookingHub" nombrado como el comando.
        Assert.True(resultado.EsExitoso);
        Assert.Contains(capturadas, span =>
            span.Source.Name == ActividadHotelBookingHub.NombreSource
            && span.DisplayName == nameof(CrearHotelCommand));
    }
}
