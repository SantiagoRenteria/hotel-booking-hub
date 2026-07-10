using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TestKit.Auth;

namespace Seguridad.FunctionalTests;

/// <summary>
/// Story 6.4 · práctica OWASP #3 (rate limiting) — el Gateway limita por cliente: superado el cupo de la
/// ventana, responde <c>429</c>. Se fija un límite bajo por config para ejercitarlo de forma determinista.
/// </summary>
public class RateLimitGatewayTests(RateLimitGatewayTests.LimiteBajoFactory factory)
    : IClassFixture<RateLimitGatewayTests.LimiteBajoFactory>
{
    /// <summary>Host del Gateway con un cupo de 3 peticiones por ventana (para disparar el 429 en la 4ª).</summary>
    public sealed class LimiteBajoFactory : WebApplicationFactory<Program>
    {
        public const int Cupo = 3;

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Issuer"] = TokenDePrueba.Issuer,
                    ["Jwt:Audience"] = TokenDePrueba.Audience,
                    ["Jwt:SigningKey"] = TokenDePrueba.SigningKey,
                    ["RateLimit:PermitLimit"] = Cupo.ToString(),
                    ["RateLimit:WindowSeconds"] = "60",
                }));

            return base.CreateHost(builder);
        }
    }

    [Fact]
    public async Task Superado_el_cupo_responde_429()
    {
        var cliente = factory.CreateClient();

        // Se usa una ruta de negocio (no /health, que está EXENTO del limitador). Sin token → 401, pero el
        // limitador corre ANTES de la autenticación, así que las primeras `Cupo` no son 429 y la siguiente sí.
        const string ruta = "/api/v1/reservas";
        for (var i = 0; i < LimiteBajoFactory.Cupo; i++)
        {
            var permitida = await cliente.GetAsync(ruta);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, permitida.StatusCode);
        }

        // La siguiente excede la ventana → 429 con Problem Details.
        var exceso = await cliente.GetAsync(ruta);
        Assert.Equal(HttpStatusCode.TooManyRequests, exceso.StatusCode);
        Assert.Equal("application/problem+json", exceso.Content.Headers.ContentType?.MediaType);
        Assert.True(exceso.Headers.Contains("Retry-After"));

        // Control: /health está EXENTO del limitador aunque se haya superado el cupo.
        var salud = await cliente.GetAsync("/health");
        Assert.NotEqual(HttpStatusCode.TooManyRequests, salud.StatusCode);
    }
}
