using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Microsoft.Extensions.DependencyInjection;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.CrearReserva;
using Reservas.Domain.Puertos;
using Reservas.Domain.Servicios;

namespace Reservas.UnitTests.CrearReserva;

/// <summary>
/// Ejercita el mediator propio (Logging + Validation, sin TransactionBehavior): el <c>ValidationBehavior</c>
/// debe cortar un comando inválido con <c>Result.Invalido</c> ANTES del handler (AC-E1.6a.2/.3), enumerando
/// el campo, y dejar pasar el válido (que STAGEA reserva + encola outbox mediante los fakes).
/// </summary>
public sealed class PipelineTests
{
    private readonly RepositorioReservasFake _repo = new();
    private readonly ColaOutboxFake _outbox = new();
    private readonly DisponibilidadHabitacionFake _dispo = new();

    private ServiceProvider Construir()
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddSingleton(new CalculadorPrecio());
        servicios.AddSingleton<IReservaRepository>(_repo);
        servicios.AddSingleton<IDisponibilidadHabitacion>(_dispo);
        servicios.AddSingleton<IColaOutbox>(_outbox);
        servicios.AddMediatorPipeline(typeof(CrearReservaCommand).Assembly);
        return servicios.BuildServiceProvider();
    }

    [Fact]
    public async Task Comando_invalido_es_cortado_por_validation_y_no_llega_al_handler()
    {
        using var proveedor = Construir();
        using var scope = proveedor.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var invalido = DatosPrueba.ComandoValido() with { AgenteEmail = "" };

        var resultado = await sender.Send(invalido, CancellationToken.None);

        Assert.Equal(EstadoResultado.Invalido, resultado.Estado);
        Assert.NotEmpty(resultado.Errores);
        Assert.Contains("AgenteEmail", resultado.Errores.Keys); // el 400 enumera el campo inválido (AC-E1.6a.2)
        Assert.Empty(_repo.Agregadas); // el handler nunca corrió
        Assert.Empty(_outbox.Encolados);
    }

    [Fact]
    public async Task Comando_valido_atraviesa_el_pipeline_hasta_el_handler()
    {
        var comando = DatosPrueba.ComandoValido();
        _dispo.Habitacion = new HabitacionReservable(
            comando.HabitacionId, Guid.NewGuid(), "Hotel Central", "Cali", Activa: true, Capacidad: 2,
            CostoBase: 100.00m, Impuesto: 19.00m);

        using var proveedor = Construir();
        using var scope = proveedor.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var resultado = await sender.Send(comando, CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Single(_repo.Agregadas);
        Assert.Single(_outbox.Encolados);
    }
}
