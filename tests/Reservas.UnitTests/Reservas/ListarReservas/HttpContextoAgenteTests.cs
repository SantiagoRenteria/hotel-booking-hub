using Microsoft.AspNetCore.Http;
using Reservas.Api;

namespace Reservas.UnitTests.ListarReservas;

/// <summary>
/// Story 3.3 — resolución de la identidad del agente desde la cabecera <c>X-Agente</c>: normalización canónica
/// (minúsculas + trim) para que el aislamiento no dependa del collation, y fail-closed ante ausencia/ambigüedad.
/// </summary>
public sealed class HttpContextoAgenteTests
{
    private static string? Resolver(params string[] valoresCabecera)
    {
        var ctx = new DefaultHttpContext();
        if (valoresCabecera.Length > 0)
        {
            ctx.Request.Headers[HttpContextoAgente.CabeceraAgente] = valoresCabecera;
        }

        var accessor = new HttpContextAccessor { HttpContext = ctx };
        return new HttpContextoAgente(accessor).AgenteActual;
    }

    [Fact]
    public void Normaliza_a_minusculas_y_recorta()
    {
        Assert.Equal("agente@test.com", Resolver("  Agente@Test.COM  "));
    }

    [Fact]
    public void Sin_cabecera_es_null_fail_closed()
    {
        Assert.Null(Resolver());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Cabecera_vacia_o_blanca_es_null(string valor)
    {
        Assert.Null(Resolver(valor));
    }

    [Fact]
    public void Cabecera_repetida_ambigua_es_null_fail_closed()
    {
        Assert.Null(Resolver("a@test.com", "b@test.com"));
    }
}
