using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Reservas.FunctionalTests;

/// <summary>
/// Story 6.2 · AC-E6.2.3 — mapa de autorización completo: NINGÚN endpoint de negocio (`/api/**`) puede quedar
/// sin metadata de autorización. Este test estructural recorre el <see cref="EndpointDataSource"/> y falla si
/// aparece un endpoint nuevo sin `RequireAuthorization` — evita el agujero "endpoint olvidado sin policy"
/// (secure-by-default verificado en CI en vez de por convención).
/// </summary>
public class CoberturaAutorizacionTests(ReservasApiFactory factory) : IClassFixture<ReservasApiFactory>
{
    [Fact]
    public void Todo_endpoint_de_negocio_declara_autorizacion()
    {
        // Forzar el arranque del host para que los endpoints estén registrados.
        _ = factory.Services;

        var fuente = factory.Services.GetRequiredService<EndpointDataSource>();

        var sinAutorizacion = fuente.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true)
            .Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        Assert.True(
            sinAutorizacion.Count == 0,
            $"Endpoints de negocio sin autorización (deben llevar RequireAuthorization): {string.Join(", ", sinAutorizacion)}");
    }
}
