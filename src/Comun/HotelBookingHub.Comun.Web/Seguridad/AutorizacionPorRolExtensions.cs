using Microsoft.Extensions.DependencyInjection;

namespace HotelBookingHub.Comun.Web.Seguridad;

/// <summary>
/// Roles de la aplicación (RBAC, Story 6.2). Valores canónicos usados tanto en el claim <c>role</c> del token
/// como en las policies server-side. Un único lugar → sin drift de strings entre servicios.
/// </summary>
public static class RolesAplicacion
{
    public const string Agente = "Agente";
    public const string Viajero = "Viajero";

    /// <summary>Tipo de claim canónico para el rol (corto, coherente con el README y el emisor de tokens).</summary>
    public const string ClaimRol = "role";
}

/// <summary>Nombres de las policies de autorización por rol (Story 6.2). Referenciadas por endpoint.</summary>
public static class PoliticasAutorizacion
{
    /// <summary>Operaciones exclusivas del agente (gestión de catálogo, resolución de cancelaciones, sus reservas).</summary>
    public const string SoloAgente = "SoloAgente";

    /// <summary>Operaciones que puede iniciar el viajero o el agente en su nombre (búsqueda, crear reserva, solicitar cancelación).</summary>
    public const string AgenteOViajero = "AgenteOViajero";
}

/// <summary>
/// Registra las policies RBAC server-side (Story 6.2). Un rol sin permiso para la operación recibe <c>403</c>
/// (resuelto por .NET Authorization, no en el cliente). Complementa la autenticación de 6.1: 6.1 exige un token
/// válido (401); 6.2 exige el rol correcto (403).
/// </summary>
public static class AutorizacionPorRolExtensions
{
    public static IServiceCollection AddAutorizacionPorRol(this IServiceCollection servicios)
    {
        servicios.AddAuthorizationBuilder()
            .AddPolicy(PoliticasAutorizacion.SoloAgente, politica =>
                politica.RequireRole(RolesAplicacion.Agente))
            .AddPolicy(PoliticasAutorizacion.AgenteOViajero, politica =>
                politica.RequireRole(RolesAplicacion.Agente, RolesAplicacion.Viajero));

        return servicios;
    }
}
