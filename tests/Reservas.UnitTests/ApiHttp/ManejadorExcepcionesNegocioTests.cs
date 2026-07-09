using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Reservas.Api.Http;
using Reservas.Domain.Reservas;

namespace Reservas.UnitTests.ApiHttp;

/// <summary>
/// Verifica el mapeo excepción de negocio → Problem Details: el overbooking arbitrado por el motor
/// (<see cref="HabitacionNoDisponibleException"/>) se traduce a 409; el resto no lo maneja este handler.
/// </summary>
public sealed class ManejadorExcepcionesNegocioTests
{
    private static DefaultHttpContext CrearContexto()
    {
        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task Overbooking_se_mapea_a_409()
    {
        var ctx = CrearContexto();

        var manejado = await new ManejadorExcepcionesNegocio()
            .TryHandleAsync(ctx, new HabitacionNoDisponibleException(), CancellationToken.None);

        Assert.True(manejado);
        Assert.Equal(StatusCodes.Status409Conflict, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Excepcion_no_de_negocio_no_la_maneja_este_handler()
    {
        var manejado = await new ManejadorExcepcionesNegocio()
            .TryHandleAsync(CrearContexto(), new InvalidOperationException("boom"), CancellationToken.None);

        Assert.False(manejado); // que caiga al pipeline por defecto (500)
    }
}
