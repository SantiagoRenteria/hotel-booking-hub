using HotelBookingHub.Comun.Resultados;
using HotelBookingHub.Comun.Web;
using Hoteles.Application.Hoteles.CrearHotel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hoteles.UnitTests.CrearHotel;

/// <summary>Verifica el mapeo transversal Result→HTTP para el recurso Hotel (201 éxito / 400 validación).</summary>
public sealed class ResultadoHttpMapeoTests
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
    public async Task Ok_se_mapea_a_201_created()
    {
        var dto = new HotelResponseDto(Guid.CreateVersion7(), "Hotel Central", "Medellín", "Habilitado");
        var http = Result<HotelResponseDto>.Ok(dto).ToCreatedResult(d => $"/api/v1/hoteles/{d.Id}");

        Assert.Equal(StatusCodes.Status201Created, await EjecutarAsync(http));
    }

    [Fact]
    public async Task Invalido_se_mapea_a_400_validation_problem()
    {
        var errores = new Dictionary<string, string[]> { ["Nombre"] = ["El nombre del hotel es obligatorio."] };
        var http = Result<HotelResponseDto>.Invalido(errores).ToCreatedResult(d => "/x");

        Assert.Equal(StatusCodes.Status400BadRequest, await EjecutarAsync(http));
    }
}
