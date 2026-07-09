using HotelBookingHub.Comun.Resultados;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HotelBookingHub.Comun.Web;

/// <summary>
/// Traducción ÚNICA y transversal de <see cref="Result{T}"/> a HTTP (sin envoltura; Problem Details RFC 7807
/// en error). Compartida por los <c>*.Api</c> de todos los BC. Los endpoints exponen un union type explícito
/// (<c>TypedResults</c>) para mantener el OpenAPI uniforme.
/// </summary>
public static class ResultadoHttpExtensions
{
    public static Results<Created<T>, ValidationProblem, ProblemHttpResult> ToCreatedResult<T>(
        this Result<T> resultado,
        Func<T, string> ubicacion) =>
        resultado.Estado switch
        {
            EstadoResultado.Ok =>
                TypedResults.Created(ubicacion(resultado.Valor!), resultado.Valor),
            EstadoResultado.Invalido =>
                TypedResults.ValidationProblem(ComoDiccionario(resultado.Errores)),
            EstadoResultado.NoEncontrado =>
                TypedResults.Problem(detail: resultado.Detalle, statusCode: StatusCodes.Status404NotFound),
            EstadoResultado.Conflicto =>
                TypedResults.Problem(detail: resultado.Detalle, statusCode: StatusCodes.Status409Conflict),
            EstadoResultado.Prohibido =>
                TypedResults.Problem(detail: resultado.Detalle, statusCode: StatusCodes.Status403Forbidden),
            _ =>
                TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };

    private static IDictionary<string, string[]> ComoDiccionario(IReadOnlyDictionary<string, string[]> errores) =>
        errores.ToDictionary(par => par.Key, par => par.Value);
}
