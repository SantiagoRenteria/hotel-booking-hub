using HotelBookingHub.Comun.Excepciones;
using HotelBookingHub.Comun.Resultados;
using HotelBookingHub.Comun.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Comun.Web.UnitTests;

/// <summary>
/// Batería del handler transversal (infraestructura compartida por todos los BC): una
/// <see cref="ExcepcionNegocio"/> se traduce al status de su <see cref="EstadoResultado"/> reutilizando la
/// única tabla <see cref="ResultadoHttpExtensions.CodigoHttp"/>; una excepción NO de negocio se deja pasar (500)
/// para no enmascarar bugs. Se prueba con un subtipo sintético para NO acoplar el test a ningún dominio.
/// </summary>
public sealed class ManejadorExcepcionesNegocioTests
{
    /// <summary>Subtipo de prueba: cualquier estado, sin depender de Reservas ni Hoteles.</summary>
    private sealed class FalloDeNegocio(EstadoResultado estado)
        : ExcepcionNegocio(estado, "detalle de prueba");

    private static DefaultHttpContext CrearContexto()
    {
        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Theory]
    [InlineData(EstadoResultado.Conflicto, StatusCodes.Status409Conflict)]
    [InlineData(EstadoResultado.NoEncontrado, StatusCodes.Status404NotFound)]
    [InlineData(EstadoResultado.Prohibido, StatusCodes.Status403Forbidden)]
    [InlineData(EstadoResultado.Invalido, StatusCodes.Status400BadRequest)]
    public async Task Excepcion_de_negocio_se_mapea_al_status_de_su_estado(EstadoResultado estado, int statusEsperado)
    {
        var ctx = CrearContexto();

        var manejado = await new ManejadorExcepcionesNegocio()
            .TryHandleAsync(ctx, new FalloDeNegocio(estado), CancellationToken.None);

        Assert.True(manejado);
        Assert.Equal(statusEsperado, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Excepcion_no_de_negocio_no_la_maneja_el_handler()
    {
        var manejado = await new ManejadorExcepcionesNegocio()
            .TryHandleAsync(CrearContexto(), new InvalidOperationException("boom"), CancellationToken.None);

        Assert.False(manejado); // que caiga al pipeline por defecto (500): un bug nunca se enmascara como negocio
    }

    [Fact]
    public async Task Conflicto_de_concurrencia_se_mapea_a_409()
    {
        var ctx = CrearContexto();

        var manejado = await new ManejadorExcepcionesNegocio()
            .TryHandleAsync(ctx, new ConflictoConcurrenciaException(new InvalidOperationException()), CancellationToken.None);

        Assert.True(manejado);
        Assert.Equal(StatusCodes.Status409Conflict, ctx.Response.StatusCode);
    }

    [Theory]
    [InlineData(EstadoResultado.Ok, StatusCodes.Status200OK)]
    [InlineData(EstadoResultado.Invalido, StatusCodes.Status400BadRequest)]
    [InlineData(EstadoResultado.NoEncontrado, StatusCodes.Status404NotFound)]
    [InlineData(EstadoResultado.Conflicto, StatusCodes.Status409Conflict)]
    [InlineData(EstadoResultado.Prohibido, StatusCodes.Status403Forbidden)]
    public void CodigoHttp_es_la_unica_tabla_estado_a_status(EstadoResultado estado, int statusEsperado)
    {
        Assert.Equal(statusEsperado, ResultadoHttpExtensions.CodigoHttp(estado));
    }
}
