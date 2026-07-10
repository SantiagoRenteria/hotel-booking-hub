using System.Diagnostics;
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
    // Longitud mínima de la clave HMAC-SHA256: 256 bits = 32 bytes (RFC 7518 §3.2).
    private const int MinBytesClaveHmac = 32;

    /// <summary>
    /// Construye los <see cref="TokenValidationParameters"/> con las cuatro validaciones de seguridad activas
    /// (issuer, audience, lifetime, firma), el algoritmo fijado a HMAC-SHA256 y un clock skew acotado. Es
    /// dominio puro de configuración → se prueba con un unit test sin levantar un host. Valida la config de
    /// forma estricta (fail-closed): clave presente y ≥ 256 bits, issuer y audience no vacíos.
    /// </summary>
    /// <exception cref="InvalidOperationException">Si la configuración JWT es inválida (fail-closed).</exception>
    public static TokenValidationParameters ConstruirParametrosValidacion(OpcionesJwt opciones)
    {
        if (string.IsNullOrWhiteSpace(opciones.SigningKey))
        {
            throw new InvalidOperationException(
                "Falta la clave de firma JWT (Jwt:SigningKey). Debe proveerse por user-secrets/variable de "
                + "entorno/Key Vault; nunca en el repositorio. Sin clave no puede validarse ningún token.");
        }

        if (Encoding.UTF8.GetByteCount(opciones.SigningKey) < MinBytesClaveHmac)
        {
            throw new InvalidOperationException(
                $"La clave de firma JWT (Jwt:SigningKey) debe tener al menos {MinBytesClaveHmac} bytes "
                + "(256 bits) para HMAC-SHA256; una clave más corta debilita la firma.");
        }

        if (string.IsNullOrWhiteSpace(opciones.Issuer))
        {
            throw new InvalidOperationException("Falta el emisor JWT (Jwt:Issuer); sin él se rechazaría todo token.");
        }

        if (string.IsNullOrWhiteSpace(opciones.Audience))
        {
            throw new InvalidOperationException("Falta la audiencia JWT (Jwt:Audience); sin ella se rechazaría todo token.");
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
            // Allow-list explícita: solo HMAC-SHA256 (defensa contra confusión de algoritmos si el día de
            // mañana se introduce una clave asimétrica; hoy la clave simétrica ya lo acota).
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            // Claim de rol CANÓNICO "role" (corto), coherente con el README y el emisor. Así RequireRole (RBAC,
            // Story 6.2) lee el claim corto del token en vez de esperar la URI larga de ClaimTypes.Role.
            RoleClaimType = RolesAplicacion.ClaimRol,
            // Acota la tolerancia de reloj (el default de la librería es 5 min); no la relajamos por encima.
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    }

    /// <summary>
    /// Registra la autenticación JWT Bearer con los parámetros de validación de <see cref="ConstruirParametrosValidacion"/>
    /// y un <c>401</c> en formato Problem Details. Enlaza <see cref="OpcionesJwt"/> desde la sección <c>Jwt</c>.
    /// Valida la configuración de forma <b>eager</b> (al arrancar): un despliegue mal configurado falla al
    /// iniciar, no con <c>500</c> opacos en el borde ante la primera petición.
    /// </summary>
    public static IServiceCollection AddAutenticacionJwt(this IServiceCollection servicios, IConfiguration configuracion)
    {
        var opciones = configuracion.GetSection(OpcionesJwt.Seccion).Get<OpcionesJwt>() ?? new OpcionesJwt();

        // Fail-fast: se valida aquí (durante el arranque) y no solo dentro del lambda perezoso de AddJwtBearer.
        var parametros = ConstruirParametrosValidacion(opciones);

        servicios
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.TokenValidationParameters = parametros;
                // Desactiva el mapeo legacy de claims (que reescribiría "role"→URI larga de ClaimTypes.Role y
                // rompería RequireRole con RoleClaimType="role"). Así sub/email/role quedan tal cual vienen.
                bearer.MapInboundClaims = false;
                // El reto por defecto responde 401 con cuerpo vacío; lo reescribimos a Problem Details RFC 7807
                // para que el contrato de errores sea uniforme con el resto del sistema.
                bearer.Events = new JwtBearerEvents
                {
                    OnChallenge = async contexto =>
                    {
                        contexto.HandleResponse();
                        await EscribirProblemDetails401(contexto.HttpContext);
                    },
                };
            });

        servicios.AddAuthorization();
        return servicios;
    }

    private static async Task EscribirProblemDetails401(HttpContext contexto)
    {
        var respuesta = contexto.Response;
        // Si otro middleware ya inició la respuesta, no podemos reescribir StatusCode/headers.
        if (respuesta.HasStarted)
        {
            return;
        }

        respuesta.StatusCode = StatusCodes.Status401Unauthorized;
        respuesta.Headers.WWWAuthenticate = "Bearer";
        respuesta.ContentType = "application/problem+json";

        var problema = new
        {
            type = "https://datatracker.ietf.org/doc/html/rfc7807",
            title = "No autenticado",
            status = StatusCodes.Status401Unauthorized,
            detail = "Se requiere un token JWT válido (issuer, audience, expiración y firma).",
            instance = contexto.Request.Path.Value,
            // Correlación con el resto del contrato de errores del sistema (W3C Trace Context).
            traceId = Activity.Current?.Id ?? contexto.TraceIdentifier,
        };

        await respuesta.WriteAsync(JsonSerializer.Serialize(problema));
    }
}
