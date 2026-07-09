using Microsoft.AspNetCore.Diagnostics;
using Reservas.Domain.Reservas;

namespace Reservas.Api.Http;

/// <summary>
/// Traduce las excepciones de negocio esperadas a Problem Details RFC 7807. El overbooking arbitrado por el
/// motor (<see cref="HabitacionNoDisponibleException"/>, 2627/2601) → 409. El resto no se maneja aquí (→ 500).
/// </summary>
public sealed class ManejadorExcepcionesNegocio : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not HabitacionNoDisponibleException)
        {
            return false; // que lo maneje el pipeline por defecto (500)
        }

        await Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status409Conflict)
            .ExecuteAsync(httpContext);
        return true;
    }
}
