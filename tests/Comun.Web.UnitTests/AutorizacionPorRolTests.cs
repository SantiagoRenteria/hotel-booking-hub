using System.Security.Claims;
using HotelBookingHub.Comun.Web.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HotelBookingHub.Comun.Web.UnitTests;

/// <summary>
/// Story 6.2 · AC-E6.2.1/.2 — las policies de rol deciden server-side quién puede invocar qué. Una policy que
/// no se cumple se traduce en <c>403</c>; que se cumpla deja pasar a negocio. Se prueba la LÓGICA de la policy
/// con <see cref="IAuthorizationService"/> (sin levantar un host); el 403 HTTP end-to-end se cubre en los tests
/// funcionales de Reservas.
/// </summary>
public class AutorizacionPorRolTests
{
    private static IAuthorizationService Servicio()
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddAuthorization();
        servicios.AddAutorizacionPorRol();
        return servicios.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    // El rol viaja en el claim CANÓNICO "role" (corto), no en la URI larga de ClaimTypes.Role (Story 6.2 fija
    // RoleClaimType = "role", cerrando la deuda de naming de 6.1). La identidad se construye con ese roleType.
    private static ClaimsPrincipal Usuario(params string[] roles)
    {
        var identidad = new ClaimsIdentity(authenticationType: "test", nameType: ClaimTypes.Name, roleType: "role");
        foreach (var rol in roles)
        {
            identidad.AddClaim(new Claim("role", rol));
        }

        return new ClaimsPrincipal(identidad);
    }

    [Theory]
    [InlineData(RolesAplicacion.Agente, true)]
    [InlineData(RolesAplicacion.Viajero, false)]
    public async Task SoloAgente_permite_solo_al_agente(string rol, bool permitido)
    {
        var resultado = await Servicio().AuthorizeAsync(Usuario(rol), resource: null, PoliticasAutorizacion.SoloAgente);

        Assert.Equal(permitido, resultado.Succeeded);
    }

    [Theory]
    [InlineData(RolesAplicacion.Agente)]
    [InlineData(RolesAplicacion.Viajero)]
    public async Task AgenteOViajero_permite_ambos_roles(string rol)
    {
        var resultado = await Servicio().AuthorizeAsync(Usuario(rol), resource: null, PoliticasAutorizacion.AgenteOViajero);

        Assert.True(resultado.Succeeded);
    }

    [Fact]
    public async Task Sin_rol_conocido_ninguna_policy_se_cumple()
    {
        var usuarioSinRol = Usuario();

        var soloAgente = await Servicio().AuthorizeAsync(usuarioSinRol, resource: null, PoliticasAutorizacion.SoloAgente);
        var agenteOViajero = await Servicio().AuthorizeAsync(usuarioSinRol, resource: null, PoliticasAutorizacion.AgenteOViajero);

        Assert.False(soloAgente.Succeeded);
        Assert.False(agenteOViajero.Succeeded);
    }
}
