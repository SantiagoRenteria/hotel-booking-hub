using Reservas.Application.Abstracciones;

namespace Reservas.Api;

/// <summary>
/// Implementación de <see cref="IContextoAgente"/> que resuelve la identidad del agente desde el claim
/// <c>email</c> del token JWT validado (Story 6.1). Reemplaza el puente <c>X-Agente</c> (Story 3.3): ahora la
/// identidad es <b>autenticada</b>, no una cabecera sin verificar. Solo cambia el adaptador — los handlers y
/// queries no se tocan porque dependen de la interfaz (esa era la promesa de la costura).
/// <para><b>Fail-closed:</b> sin claim de identidad, <see cref="AgenteActual"/> es null → el handler corta con
/// 403/404. Normalización canónica (minúsculas + trim) idéntica a la de <c>Reserva.AgenteEmail</c>, para que el
/// aislamiento no dependa del collation de SQL.</para>
/// </summary>
public sealed class ClaimContextoAgente(IHttpContextAccessor accessor) : IContextoAgente
{
    /// <summary>Tipo de claim de identidad. Con <c>MapInboundClaims=false</c> viaja crudo como <c>email</c> (RFC 7519).</summary>
    public const string ClaimEmail = "email";

    public string? AgenteActual
    {
        get
        {
            var emails = accessor.HttpContext?.User.FindAll(ClaimEmail).ToList();
            // Ausente o AMBIGUO (varios claims `email`) → sin identidad (fail-closed): no adivinamos cuál es.
            if (emails is not { Count: 1 })
            {
                return null;
            }

            var valor = emails[0].Value;
            return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim().ToLowerInvariant();
        }
    }
}
