using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TestKit.Auth;

namespace Reservas.FunctionalTests;

/// <summary>
/// Host de test de <c>Reservas.Api</c> con config JWT determinista + una cadena de conexión ficticia. El RBAC
/// (403) y el aislamiento se resuelven en la AUTORIZACIÓN, antes del handler → no tocan la base de datos. El
/// <c>RelayOutbox</c> (BackgroundService) captura sus excepciones por ciclo, así que el host arranca sin infra.
/// </summary>
public sealed class ReservasApiFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = TokenDePrueba.Issuer,
                ["Jwt:Audience"] = TokenDePrueba.Audience,
                ["Jwt:SigningKey"] = TokenDePrueba.SigningKey,
                // Cadena ficticia: EF la configura de forma perezosa; solo el 5xx de un handler que consulte
                // la revelaría, nunca las rutas de autorización (403) que este proyecto ejercita.
                ["ConnectionStrings:reservasdb"] = "Server=localhost;Database=test;Trusted_Connection=False;",
            }));

        return base.CreateHost(builder);
    }
}
