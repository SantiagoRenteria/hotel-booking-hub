using Hoteles.Application.Abstracciones;

namespace Hoteles.Api;

/// <summary>
/// Implementación de <see cref="IContextoAgente"/> que resuelve la identidad del agente desde el claim
/// <c>email</c> del token JWT validado (Story 6.3). Normalización canónica (minúsculas + trim) idéntica a
/// <c>Hotel.AgentePropietario</c>, para que el aislamiento no dependa del collation de SQL. Fail-closed: sin
/// claim de identidad → null.
/// </summary>
public sealed class ClaimContextoAgente(IHttpContextAccessor accessor) : IContextoAgente
{
    /// <summary>Con <c>MapInboundClaims=false</c> el claim viaja crudo como <c>email</c> (RFC 7519).</summary>
    public const string ClaimEmail = "email";

    public string? AgenteActual
    {
        get
        {
            var valor = accessor.HttpContext?.User.FindFirst(ClaimEmail)?.Value;
            return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim().ToLowerInvariant();
        }
    }
}
