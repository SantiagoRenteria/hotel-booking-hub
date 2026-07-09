using System.Text.RegularExpressions;

namespace Reservas.Application.Reservas.CrearReserva;

/// <summary>
/// Expresiones regulares de validación con <b>matchTimeout explícito</b> (anti-ReDoS,
/// fuente <c>security-and-quality.md</c>). Los patrones son lineales; el timeout es defensa en profundidad.
/// </summary>
internal static partial class ExpresionesValidacion
{
    /// <summary>Teléfono E.164 laxo: dígitos con un '+' opcional al inicio.</summary>
    [GeneratedRegex(@"^\+?\d{7,15}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    public static partial Regex Telefono();

    /// <summary>Número de documento: alfanumérico con guiones, 4–20 caracteres.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9\-]{4,20}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    public static partial Regex NumeroDocumento();
}
