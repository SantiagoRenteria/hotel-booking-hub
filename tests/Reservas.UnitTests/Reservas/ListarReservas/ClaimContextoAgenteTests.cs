using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Reservas.Api;

namespace Reservas.UnitTests.ListarReservas;

/// <summary>
/// Story 6.3 — la identidad del agente se resuelve desde el claim <c>email</c> del token JWT validado (6.1),
/// reemplazando la cabecera <c>X-Agente</c> (puente de 3.3). Conserva las garantías de la costura: normalización
/// canónica (minúsculas + trim, aislamiento independiente del collation) y fail-closed ante ausencia.
/// </summary>
public sealed class ClaimContextoAgenteTests
{
    private static string? Resolver(params (string tipo, string valor)[] claims)
    {
        var ctx = new DefaultHttpContext();
        if (claims.Length > 0)
        {
            var identidad = new ClaimsIdentity(claims.Select(c => new Claim(c.tipo, c.valor)), "test");
            ctx.User = new ClaimsPrincipal(identidad);
        }

        var accessor = new HttpContextAccessor { HttpContext = ctx };
        return new ClaimContextoAgente(accessor).AgenteActual;
    }

    [Fact]
    public void Normaliza_el_email_del_claim_a_minusculas_y_recorta()
    {
        Assert.Equal("agente@test.com", Resolver((ClaimContextoAgente.ClaimEmail, "  Agente@Test.COM  ")));
    }

    [Fact]
    public void Sin_claim_de_email_es_null_fail_closed()
    {
        Assert.Null(Resolver());
    }

    [Fact]
    public void Sin_autenticar_es_null_fail_closed()
    {
        Assert.Null(Resolver((ClaimTypes.Name, "algo")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Email_vacio_o_blanco_es_null(string valor)
    {
        Assert.Null(Resolver((ClaimContextoAgente.ClaimEmail, valor)));
    }

    [Fact]
    public void Dos_claims_email_es_ambiguo_y_null_fail_closed()
    {
        // Un token con múltiples claims `email` es ambiguo: no adivinamos la identidad → fail-closed (review 6.3).
        Assert.Null(Resolver(
            (ClaimContextoAgente.ClaimEmail, "a@test.com"),
            (ClaimContextoAgente.ClaimEmail, "b@test.com")));
    }
}
