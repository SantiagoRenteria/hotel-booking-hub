using HotelBookingHub.Comun.Resultados;
using HotelBookingHub.Comun.Web;
using Hoteles.Application.Habitaciones;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hoteles.UnitTests.Habitaciones;

/// <summary>Mapeo Result→HTTP para el recurso Habitación (201 alta / 200 edición-estado / 404).</summary>
public sealed class HabitacionResultadoHttpMapeoTests
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

    private static HabitacionResponseDto Dto() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "Suite", 100m, 19m, "Piso 3", "Habilitada", "AAAAAAAAB9E=");

    [Fact]
    public async Task Alta_ok_se_mapea_a_201()
    {
        var http = Result<HabitacionResponseDto>.Ok(Dto()).ToCreatedResult(d => $"/api/v1/habitaciones/{d.Id}");
        Assert.Equal(StatusCodes.Status201Created, await EjecutarAsync(http));
    }

    [Fact]
    public async Task Edicion_ok_se_mapea_a_200()
    {
        var http = Result<HabitacionResponseDto>.Ok(Dto()).ToOkResult();
        Assert.Equal(StatusCodes.Status200OK, await EjecutarAsync(http));
    }

    [Fact]
    public async Task No_encontrada_se_mapea_a_404()
    {
        var http = Result<HabitacionResponseDto>.NoEncontrado("No existe.").ToOkResult();
        Assert.Equal(StatusCodes.Status404NotFound, await EjecutarAsync(http));
    }
}
