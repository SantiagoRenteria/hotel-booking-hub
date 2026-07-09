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
            var valor = accessor.HttpContext?.Request.Headers[CabeceraAgente].ToString();
            return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
        }
    }
}
