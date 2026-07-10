using HotelBookingHub.Comun.Web.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Reservas.FunctionalTests;

/// <summary>
/// Story 6.2 · AC-E6.2.3 — mapa de autorización COMPLETO y CORRECTO: cada endpoint de negocio de Reservas.Api
/// debe declarar exactamente la policy esperada. Recorre el <see cref="EndpointDataSource"/> y compara el
/// conjunto (método + ruta → policy) real contra el esperado. Falla si aparece un endpoint nuevo sin entrada,
/// si falta uno, o si la policy difiere (p. ej. un `SoloAgente` degradado a `AgenteOViajero` o a sin-policy) —
/// cerrando el hueco de "escalada por policy incorrecta" que un chequeo de mera presencia no detecta.
/// </summary>
public class CoberturaAutorizacionTests(ReservasApiFactory factory) : IClassFixture<ReservasApiFactory>
{
    [Fact]
    public void El_mapa_rol_endpoint_coincide_exactamente_con_lo_esperado()
    {
        // Mapa esperado: "MÉTODO ruta" (ruta sin barra inicial) → policy. Debe coincidir con Reservas.Api/Program.cs.
        var esperado = new Dictionary<string, string>
        {
            ["POST api/v1/reservas"] = PoliticasAutorizacion.AgenteOViajero,
            ["POST api/v1/reservas/{id:guid}/solicitud-cancelacion"] = PoliticasAutorizacion.AgenteOViajero,
            ["POST api/v1/reservas/{id:guid}/cancelacion/resolucion"] = PoliticasAutorizacion.SoloAgente,
            ["POST api/v1/reservas/{id:guid}/cancelaciones/atajo"] = PoliticasAutorizacion.SoloAgente,
            ["GET api/v1/reservas/cancelaciones-pendientes"] = PoliticasAutorizacion.SoloAgente,
            ["GET api/v1/habitaciones/disponibles"] = PoliticasAutorizacion.AgenteOViajero,
            ["GET api/v1/reservas"] = PoliticasAutorizacion.SoloAgente,
            ["GET api/v1/reservas/{id:guid}"] = PoliticasAutorizacion.SoloAgente,
        };

        _ = factory.Services; // fuerza el arranque del host → endpoints registrados

        var real = new Dictionary<string, string?>();
        foreach (var endpoint in factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var ruta = endpoint.RoutePattern.RawText?.TrimStart('/') ?? string.Empty;
            // Solo endpoints de negocio: se excluyen health/alive/openapi (nunca bajo /api).
            if (!ruta.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var metodo = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? "?";
            var policy = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => a.Policy)
                .FirstOrDefault(p => !string.IsNullOrEmpty(p));

            real[$"{metodo} {ruta}"] = policy;
        }

        // Guard anti-vacuo: si el filtro no encuentra endpoints, el test no verifica nada → debe fallar.
        Assert.NotEmpty(real);

        // Conjunto exacto de endpoints (detecta uno nuevo sin entrada o uno faltante).
        Assert.Equal(esperado.Keys.OrderBy(k => k), real.Keys.OrderBy(k => k));

        // Policy exacta por endpoint (detecta degradación o policy equivocada = escalada).
        foreach (var (clave, policyEsperada) in esperado)
        {
            Assert.Equal(policyEsperada, real[clave]);
        }
    }
}
