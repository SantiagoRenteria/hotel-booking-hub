using HotelBookingHub.Comun.Resultados;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HotelBookingHub.Comun.Web;

/// <summary>
/// Traducción ÚNICA y transversal de <see cref="Result{T}"/>/<see cref="Result"/> a HTTP (sin envoltura;
/// Problem Details RFC 7807 en error). Compartida por los <c>*.Api</c> de todos los BC. Los endpoints exponen
/// un union type explícito (<c>TypedResults</c>) para mantener el OpenAPI uniforme.
/// <para>
/// <see cref="CodigoHttp"/> es la <b>única tabla</b> <c>EstadoResultado→status HTTP</c> del sistema: la usan
/// tanto estos mapeos como el <see cref="ManejadorExcepcionesNegocio"/> (excepciones de negocio). Así el mismo
/// <see cref="EstadoResultado.Conflicto"/> produce 409 venga por <see cref="Result"/> o por excepción.
/// </para>
/// </summary>
public static class ResultadoHttpExtensions
{
    /// <summary>Única tabla <c>EstadoResultado → status HTTP</c>. Un estado no contemplado degrada a 500.</summary>
    public static int CodigoHttp(EstadoResultado estado) => estado switch
    {
        EstadoResultado.Ok => StatusCodes.Status200OK,
        EstadoResultado.Invalido => StatusCodes.Status400BadRequest,
        EstadoResultado.NoEncontrado => StatusCodes.Status404NotFound,
        EstadoResultado.Conflicto => StatusCodes.Status409Conflict,
        EstadoResultado.Prohibido => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>Éxito → <c>201 Created</c> con <c>Location</c>; fallos → Problem Details / ValidationProblem.</summary>
    public static Results<Created<T>, ValidationProblem, ProblemHttpResult> ToCreatedResult<T>(
        this Result<T> resultado,
        Func<T, string> ubicacion) =>
        resultado.Estado switch
        {
            EstadoResultado.Ok => TypedResults.Created(ubicacion(resultado.Valor!), resultado.Valor),
            EstadoResultado.Invalido => TypedResults.ValidationProblem(ComoDiccionario(resultado.Errores)),
            _ => TypedResults.Problem(detail: resultado.Detalle, statusCode: CodigoHttp(resultado.Estado)),
        };

    /// <summary>Éxito → <c>200 OK</c> con el valor en el cuerpo; fallos → Problem Details / ValidationProblem.</summary>
    public static Results<Ok<T>, ValidationProblem, ProblemHttpResult> ToOkResult<T>(this Result<T> resultado) =>
        resultado.Estado switch
        {
            EstadoResultado.Ok => TypedResults.Ok(resultado.Valor),
            EstadoResultado.Invalido => TypedResults.ValidationProblem(ComoDiccionario(resultado.Errores)),
            _ => TypedResults.Problem(detail: resultado.Detalle, statusCode: CodigoHttp(resultado.Estado)),
        };

    /// <summary>Éxito → <c>204 No Content</c>; fallos → Problem Details / ValidationProblem.</summary>
    public static Results<NoContent, ValidationProblem, ProblemHttpResult> ToNoContentResult(this Result resultado) =>
        resultado.Estado switch
        {
            EstadoResultado.Ok => TypedResults.NoContent(),
            EstadoResultado.Invalido => TypedResults.ValidationProblem(ComoDiccionario(resultado.Errores)),
            _ => TypedResults.Problem(detail: resultado.Detalle, statusCode: CodigoHttp(resultado.Estado)),
        };

    private static IDictionary<string, string[]> ComoDiccionario(IReadOnlyDictionary<string, string[]> errores) =>
        errores.ToDictionary(par => par.Key, par => par.Value);
}
