using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Hoteles.CrearHotel;
using Hoteles.Domain.Hoteles;
using Hoteles.Domain.Puertos;
using Microsoft.Extensions.DependencyInjection;

namespace Hoteles.UnitTests.CrearHotel;

/// <summary>
/// Ejercita el mediator (Logging + Validation): un comando inválido se corta con `Result.Invalido` (→ 400)
/// ANTES del handler (AC-E2.1.2, enumerando el campo); el válido llega al handler y persiste vía el fake.
/// </summary>
public sealed class PipelineTests
{
    private readonly HotelRepositoryFake _repo = new();

    private ServiceProvider Construir()
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddSingleton<IHotelRepository>(_repo);
        servicios.AddMediatorPipeline(typeof(CrearHotelCommand).Assembly);
        return servicios.BuildServiceProvider();
    }

    [Fact]
    public async Task Comando_invalido_es_cortado_por_validation_y_no_llega_al_handler()
    {
        using var proveedor = Construir();
        using var scope = proveedor.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var invalido = new CrearHotelCommand("", "Medellín", "", "", EstadoHotel.Habilitado);

        var resultado = await sender.Send(invalido, CancellationToken.None);

        Assert.Equal(EstadoResultado.Invalido, resultado.Estado);
        Assert.Contains("Nombre", resultado.Errores.Keys);
        Assert.Empty(_repo.Creados);
    }

    [Fact]
    public async Task Comando_valido_atraviesa_el_pipeline_hasta_el_handler()
    {
        using var proveedor = Construir();
        using var scope = proveedor.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var comando = new CrearHotelCommand("Hotel Central", "Medellín", "Calle 1", "desc", EstadoHotel.Habilitado);

        var resultado = await sender.Send(comando, CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Single(_repo.Creados);
    }
}
