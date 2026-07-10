using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace HotelBookingHub.Comun.Web.Seguridad;

/// <summary>
/// Opciones de validación del token JWT, enlazadas desde la sección <c>Jwt</c> de configuración. El
/// <see cref="Issuer"/> y el <see cref="Audience"/> son valores NO sensibles (pueden vivir en appsettings);
/// la <see cref="SigningKey"/> es un SECRETO y debe venir de user-secrets/variable de entorno/Key Vault,
/// NUNCA de un archivo del repositorio (Story 6.1, AC-E6.1.3 · práctica OWASP #5).
/// </summary>
public sealed record OpcionesJwt
{
    public const string Seccion = "Jwt";

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string SigningKey { get; init; } = string.Empty;
}

/// <summary>
/// Helper compartido de autenticación JWT (Story 6.1). Lo invocan el Gateway y cada <c>*.Api</c> para
/// configurar la validación de forma idéntica (sin drift): issuer, audience, expiración y firma verificados
/// server-side. El <c>401</c> se emite como Problem Details RFC 7807, coherente con el resto del sistema.
/// </summary>
public static class AutenticacionJwtExtensions
{
    /// <summary>
    /// Construye los <see cref="TokenValidationParameters"/> con las cuatro validaciones de seguridad activas
    /// (issuer, audience, lifetime, firma) y un clock skew acotado. Es dominio puro de configuración → se
    /// prueba con un unit test sin levantar un host.
    /// </summary>
    /// <exception cref="InvalidOperationException">Si falta la clave de firma (fail-closed: sin clave no se valida nada).</exception>
    public static TokenValidationParameters ConstruirParametrosValidacion(OpcionesJwt opciones)
    {
        if (string.IsNullOrWhiteSpace(opciones.SigningKey))
        {
            throw new InvalidOperationException(
                "Falta la clave de firma JWT (Jwt:SigningKey). Debe proveerse por user-secrets/variable de "
                + "entorno/Key Vault; nunca en el repositorio. Sin clave no puede validarse ningún token.");
        }

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = opciones.Issuer,
            ValidAudience = opciones.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opciones.SigningKey)),
            // Acota la tolerancia de reloj (el default de la librería es 5 min); no la relajamos por encima.
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    }

    /// <summary>
    /// Registra la autenticación JWT Bearer con los parámetros de validación de <see cref="ConstruirParametrosValidacion"/>
    /// y un <c>401</c> en formato Problem Details. Enlaza <see cref="OpcionesJwt"/> desde la sección <c>Jwt</c>.
    /// </summary>
    public static IServiceCollection AddAutenticacionJwt(this IServiceCollection servicios, IConfiguration configuracion)
    {
        var opciones = configuracion.GetSection(OpcionesJwt.Seccion).Get<OpcionesJwt>() ?? new OpcionesJwt();

        servicios
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.TokenValidationParameters = ConstruirParametrosValidacion(opciones);
                // El reto por defecto responde 401 con cuerpo vacío; lo reescribimos a Problem Details RFC 7807
                // para que el contrato de errores sea uniforme con el resto del sistema.
                bearer.Events = new JwtBearerEvents
                {
                    OnChallenge = async contexto =>
                    {
                        contexto.HandleResponse();
                        await EscribirProblemDetails401(contexto.Response);
                    },
                };
            });

        servicios.AddAuthorization();
        return servicios;
    }

    private static async Task EscribirProblemDetails401(HttpResponse respuesta)
    {
        respuesta.StatusCode = StatusCodes.Status401Unauthorized;
        respuesta.Headers.WWWAuthenticate = "Bearer";
        respuesta.ContentType = "application/problem+json";

        var problema = new
        {
            type = "https://tools.ietf.org/html/rfc7235#section-3.1",
            title = "No autenticado",
            status = StatusCodes.Status401Unauthorized,
            detail = "Se requiere un token JWT válido (issuer, audience, expiración y firma).",
        };

        await respuesta.WriteAsync(JsonSerializer.Serialize(problema));
    }
}
