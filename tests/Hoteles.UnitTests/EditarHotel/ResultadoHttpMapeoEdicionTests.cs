using HotelBookingHub.Comun.Resultados;
using HotelBookingHub.Comun.Web;
using Hoteles.Application.Hoteles;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hoteles.UnitTests.EditarHotel;

/// <summary>
/// Mapeo transversal Result→HTTP para edición (<c>ToOkResult</c> → 200) y baja (<c>ToNoContentResult</c> → 204),
/// con sus fallos de negocio (404/409). Complementa la batería de <c>CodigoHttp</c> en Comun.Web.UnitTests.
/// </summary>
public sealed class ResultadoHttpMapeoEdicionTests
{
    private static async Task<int> EjecutarAsync(IResult resultado)
    {
        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        ctx.Response.Body = new MemoryStream();
        await resultado.ExecuteAsync(ctx);
        return ctx.Response.StatusCode;
    }

    [Fact]
    public async Task Edicion_ok_se_mapea_a_200()
    {
        var dto = new HotelResponseDto(Guid.CreateVersion7(), "Hotel Central", "Medellín", "Habilitado", "AAAAAAAAB9E=");
        var http = Result<HotelResponseDto>.Ok(dto).ToOkResult();

        Assert.Equal(StatusCodes.Status200OK, await EjecutarAsync(http));
    }

    [Fact]
    public async Task Edicion_no_encontrada_se_mapea_a_404()
    {
        var http = Result<HotelResponseDto>.NoEncontrado("No existe.").ToOkResult();

        Assert.Equal(StatusCodes.Status404NotFound, await EjecutarAsync(http));
    }

    [Fact]
    public async Task Edicion_en_conflicto_se_mapea_a_409()
    {
        var http = Result<HotelResponseDto>.Conflicto("Recargue y reintente.").ToOkResult();

        Assert.Equal(StatusCodes.Status409Conflict, await EjecutarAsync(http));
    }

    [Fact]
    public async Task Baja_ok_se_mapea_a_204()
    {
        var http = Result.Ok().ToNoContentResult();

        Assert.Equal(StatusCodes.Status204NoContent, await EjecutarAsync(http));
    }

    [Fact]
    public async Task Baja_no_encontrada_se_mapea_a_404()
    {
        var http = Result.NoEncontrado("No existe.").ToNoContentResult();

        Assert.Equal(StatusCodes.Status404NotFound, await EjecutarAsync(http));
    }
}
