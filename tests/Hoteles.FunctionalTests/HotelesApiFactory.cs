using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TestKit.Auth;

namespace Hoteles.FunctionalTests;

/// <summary>
/// Host de test de <c>Hoteles.Api</c> con config JWT determinista + cadena de conexión ficticia. El RBAC (403)
/// se resuelve en la autorización antes del handler → no toca la BD. El <c>RelayOutbox</c> captura sus errores
/// por ciclo, así que el host arranca sin infra.
/// </summary>
public sealed class HotelesApiFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = TokenDePrueba.Issuer,
                ["Jwt:Audience"] = TokenDePrueba.Audience,
                ["Jwt:SigningKey"] = TokenDePrueba.SigningKey,
                ["ConnectionStrings:hotelesdb"] = "Server=localhost;Database=test;Trusted_Connection=False;",
            }));

        return base.CreateHost(builder);
    }
}
