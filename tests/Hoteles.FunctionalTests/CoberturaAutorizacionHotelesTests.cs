using System.Net;
using System.Net.Http.Headers;
using HotelBookingHub.Comun.Web.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TestKit.Auth;

namespace Hoteles.FunctionalTests;

/// <summary>
/// Story 6.2 · AC-E6.2.1/.3 — toda la gestión de catálogo de Hoteles.Api es exclusiva del rol Agente. El gate
/// estructural exige que CADA endpoint `/api/**` lleve la policy `SoloAgente` (secure-by-default verificado en
/// CI para Hoteles, no solo Reservas), y un test HTTP confirma el 403 de un rol sin permiso.
/// </summary>
public class CoberturaAutorizacionHotelesTests(HotelesApiFactory factory) : IClassFixture<HotelesApiFactory>
{
    [Fact]
    public void Todo_endpoint_de_gestion_exige_SoloAgente()
    {
        _ = factory.Services;

        var negocio = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText?.TrimStart('/') ?? string.Empty)
                .StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Guard anti-vacuo + confirma que están los 9 endpoints de gestión.
        Assert.Equal(9, negocio.Count);

        foreach (var endpoint in negocio)
        {
            var policy = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => a.Policy)
                .FirstOrDefault(p => !string.IsNullOrEmpty(p));

            Assert.Equal(PoliticasAutorizacion.SoloAgente, policy);
        }
    }

    [Fact]
    public async Task Viajero_al_crear_hotel_responde_403()
    {
        var cliente = factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenDePrueba.Emitir(rol: TokenDePrueba.RolViajero));

        // Sin cuerpo: la autorización (SoloAgente) rechaza antes del model binding → 403 (rol sin permiso).
        var respuesta = await cliente.PostAsync("/api/v1/hoteles", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }
}
