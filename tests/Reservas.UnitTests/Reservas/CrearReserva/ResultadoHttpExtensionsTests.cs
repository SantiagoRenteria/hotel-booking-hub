using HotelBookingHub.Comun.Resultados;
using HotelBookingHub.Comun.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Reservas.Application.Reservas.CrearReserva;

namespace Reservas.UnitTests.CrearReserva;

/// <summary>
/// Verifica el mapeo centralizado Result→HTTP ejecutando el <see cref="IResult"/> contra un contexto real
/// y comprobando el código de estado (201 éxito / 400 validación / 409 conflicto / 404 no encontrado).
/// </summary>
public sealed class ResultadoHttpExtensionsTests
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
        var dto = new ReservaResponseDto(Guid.CreateVersion7(), "Confirmada", 240.98m);
        var http = Result<ReservaResponseDto>.Ok(dto).ToCreatedResult(d => $"/api/v1/reservas/{d.Id}");

        Assert.Equal(StatusCodes.Status201Created, await EjecutarAsync(http));
    }

    [Fact]
    public async Task Invalido_se_mapea_a_400_validation_problem()
    {
        var errores = new Dictionary<string, string[]> { ["Email"] = ["El email es obligatorio."] };
        var http = Result<ReservaResponseDto>.Invalido(errores).ToCreatedResult(d => "/x");

        Assert.Equal(StatusCodes.Status400BadRequest, await EjecutarAsync(http));
    }

    [Fact]
    public async Task Conflicto_se_mapea_a_409()
    {
        var http = Result<ReservaResponseDto>.Conflicto("La habitación no está disponible.").ToCreatedResult(d => "/x");

        Assert.Equal(StatusCodes.Status409Conflict, await EjecutarAsync(http));
    }

    [Fact]
    public async Task No_encontrado_se_mapea_a_404()
    {
        var http = Result<ReservaResponseDto>.NoEncontrado("La habitación no existe.").ToCreatedResult(d => "/x");

        Assert.Equal(StatusCodes.Status404NotFound, await EjecutarAsync(http));
    }
}
