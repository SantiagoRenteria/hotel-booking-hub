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

        // Las primeras `Cupo` peticiones pasan el limitador (health es anónimo; el limitador corre antes de auth).
        for (var i = 0; i < LimiteBajoFactory.Cupo; i++)
        {
            var ok = await cliente.GetAsync("/health");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, ok.StatusCode);
        }

        // La siguiente excede la ventana → 429.
        var exceso = await cliente.GetAsync("/health");
        Assert.Equal(HttpStatusCode.TooManyRequests, exceso.StatusCode);
    }
}
