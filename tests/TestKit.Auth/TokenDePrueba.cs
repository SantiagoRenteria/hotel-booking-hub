using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TestKit.Auth;

/// <summary>
/// Emisor de JWTs de PRUEBA (issuer/audience/clave deterministas, solo en el proceso de test — cero secretos en
/// el repo). El rol viaja en el claim canónico <c>role</c> (corto), coherente con <c>RoleClaimType</c> del sistema.
/// Los parámetros permiten fabricar tokens inválidos (issuer/audience/clave/expiración erróneos) para los AC negativos.
/// </summary>
public static class TokenDePrueba
{
    public const string Issuer = "hotel-booking-hub-test";
    public const string Audience = "hotel-booking-hub-api-test";

    // Clave de PRUEBA (≥256 bits para HMAC-SHA256). Solo vive en el proceso de test.
    public const string SigningKey = "clave-de-prueba-solo-para-tests-funcionales-256bits-hmacsha256"; // gitleaks:allow (constante de test)

    public const string RolAgente = "Agente";
    public const string RolViajero = "Viajero";

    public static string Emitir(
        string? rol = RolAgente,
        string? issuer = Issuer,
        string? audience = Audience,
        string? signingKey = SigningKey,
        DateTime? expira = null,
        string email = "agente@ejemplo.com")
    {
        var clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? SigningKey));
        var credenciales = new SigningCredentials(clave, SecurityAlgorithms.HmacSha256);

        var expiracion = expira ?? DateTime.UtcNow.AddHours(1);
        // notBefore SIEMPRE anterior a expires (evita el IDX12401 al fabricar un token ya expirado).
        var noAntesDe = expiracion.AddHours(-2);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, email),
            new(JwtRegisteredClaimNames.Email, email),
        };
        if (!string.IsNullOrEmpty(rol))
        {
            claims.Add(new Claim("role", rol));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: noAntesDe,
            expires: expiracion,
            signingCredentials: credenciales);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
