using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Seguridad.FunctionalTests;

/// <summary>
/// Host de test del <c>ApiGateway</c> con configuración JWT determinista (issuer/audience/clave inyectados
/// en memoria — NO se lee ninguna clave del repositorio). Emite tokens con la misma clave para ejercitar el
/// borde de autenticación end-to-end (Story 6.1).
/// </summary>
public sealed class JwtTestFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "hotel-booking-hub-test";
    public const string Audience = "hotel-booking-hub-api-test";

    // Clave de PRUEBA (≥256 bits para HMAC-SHA256). Solo vive en el proceso de test.
    public const string SigningKey = "clave-de-prueba-solo-para-tests-funcionales-256bits-hmacsha256";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:SigningKey"] = SigningKey,
            }));

        return base.CreateHost(builder);
    }

    /// <summary>Emite un JWT firmado. Los parámetros permiten fabricar tokens inválidos (issuer/audience/clave/expiración erróneos).</summary>
    public static string EmitirToken(
        string? issuer = Issuer,
        string? audience = Audience,
        string? signingKey = SigningKey,
        DateTime? expira = null,
        string rol = "Agente",
        string email = "agente@ejemplo.com")
    {
        var clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? SigningKey));
        var credenciales = new SigningCredentials(clave, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, email),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(ClaimTypes.Role, rol),
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-5),
            expires: expira ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: credenciales);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
