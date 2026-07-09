using HotelBookingHub.Comun.Excepciones;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace HotelBookingHub.Comun.Web;

/// <summary>
/// Handler transversal que traduce las <see cref="ExcepcionNegocio"/> (overbooking, conflicto de concurrencia,
/// etc.) a Problem Details RFC 7807, reutilizando la ÚNICA tabla <c>EstadoResultado→HTTP</c>
/// (<see cref="ResultadoHttpExtensions.CodigoHttp"/>). Lo comparten todos los <c>*.Api</c>; sustituye a los
/// handlers por-servicio (decisión Story 2.2 — Alternativa C).
/// <para>
/// Detecta por el tipo base, no por subtipos concretos: así lo transversal NO depende de los dominios de cada
/// BC. Cualquier excepción que NO sea de negocio se deja pasar (<c>return false</c>) para que caiga al 500 por
/// defecto — un bug nunca se enmascara como respuesta «de negocio».
/// </para>
/// </summary>
public sealed class ManejadorExcepcionesNegocio : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ExcepcionNegocio negocio)
        {
            return false; // no es de negocio → lo maneja el pipeline por defecto (500). No se traga el bug.
        }

        await Results.Problem(detail: negocio.Message, statusCode: ResultadoHttpExtensions.CodigoHttp(negocio.Estado))
            .ExecuteAsync(httpContext);
        return true;
    }
}
