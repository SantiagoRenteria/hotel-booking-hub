using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TestKit.Auth;

namespace Seguridad.FunctionalTests;

/// <summary>
/// Host de test del <c>ApiGateway</c> con configuración JWT determinista (issuer/audience/clave de
/// <see cref="TokenDePrueba"/>, inyectados en memoria — NO se lee ninguna clave del repositorio). Ejercita el
/// borde de autenticación end-to-end (Story 6.1).
/// </summary>
public sealed class JwtTestFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Se usa ConfigureHostConfiguration (no App): la validación EAGER de la clave corre en
        // AddAutenticacionJwt al construir el host, así que la config debe estar disponible ANTES —
        // la host-config lo está y, en el host de test, tiene precedencia sobre appsettings.json.
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = TokenDePrueba.Issuer,
                ["Jwt:Audience"] = TokenDePrueba.Audience,
                ["Jwt:SigningKey"] = TokenDePrueba.SigningKey,
            }));

        return base.CreateHost(builder);
    }
}
