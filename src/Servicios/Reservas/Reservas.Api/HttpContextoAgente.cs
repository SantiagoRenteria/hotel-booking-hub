using Microsoft.Extensions.Primitives;
using Reservas.Application.Abstracciones;

namespace Reservas.Api;

/// <summary>
/// Implementación de <see cref="IContextoAgente"/> para el alcance actual (sin auth real): resuelve la identidad
/// del agente desde la cabecera <c>X-Agente</c> de la petición, server-side. Es un <b>puente explícito</b> hasta
/// la Épica 6 (seguridad), donde se sustituye por una lectura del claim del token — sin tocar handlers ni queries.
/// NO es autenticación: no valida credenciales, solo transporta identidad.
/// </summary>
public sealed class HttpContextoAgente(IHttpContextAccessor accessor) : IContextoAgente
{
    public const string CabeceraAgente = "X-Agente";

    public string? AgenteActual
    {
        get
        {
            var valores = accessor.HttpContext?.Request.Headers[CabeceraAgente] ?? StringValues.Empty;

            // Ausente o AMBIGUO (varios valores) → sin identidad (fail-closed): no adivinamos a qué agente se refiere.
            if (valores.Count != 1)
            {
                return null;
            }

            var valor = valores[0];
            // Normalización canónica (minúsculas + trim): el email es case-insensitive; así el aislamiento NO
            // depende del collation de SQL y coincide con el AgenteEmail guardado (normalizado igual en Reserva).
            return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim().ToLowerInvariant();
        }
    }
}
