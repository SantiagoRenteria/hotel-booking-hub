namespace HotelBookingHub.Comun.Resultados;

/// <summary>Estado de un <see cref="Result"/>: éxito o una de las familias de fallo esperado.</summary>
public enum EstadoResultado
{
    Ok,
    Invalido,      // → 400 (validación): errores por campo (RFC 7807 ValidationProblem)
    NoEncontrado,  // → 404
    Conflicto,     // → 409 (p. ej. habitación no disponible)
    Prohibido,     // → 403
}

/// <summary>
/// Patrón Result para flujos esperados: los handlers devuelven <see cref="Result"/>/<see cref="Result{T}"/>
/// en vez de lanzar (400/404/409/403 son datos, no excepciones). El mapeo a HTTP se centraliza en la capa Api.
/// </summary>
public class Result
{
    private static readonly IReadOnlyDictionary<string, string[]> _sinErrores =
        new Dictionary<string, string[]>();

    public EstadoResultado Estado { get; }

    /// <summary>Errores de validación por campo (solo en <see cref="EstadoResultado.Invalido"/>).</summary>
    public IReadOnlyDictionary<string, string[]> Errores { get; }

    /// <summary>Detalle legible del fallo de negocio (404/409/403); en español, con tildes.</summary>
    public string? Detalle { get; }

    public bool EsExitoso => Estado == EstadoResultado.Ok;

    protected Result(EstadoResultado estado, IReadOnlyDictionary<string, string[]>? errores, string? detalle)
    {
        Estado = estado;
        Errores = errores ?? _sinErrores;
        Detalle = detalle;
    }

    public static Result Ok() => new(EstadoResultado.Ok, null, null);

    public static Result Invalido(IReadOnlyDictionary<string, string[]> errores) =>
        new(EstadoResultado.Invalido, errores, null);

    public static Result NoEncontrado(string detalle) => new(EstadoResultado.NoEncontrado, null, detalle);

    public static Result Conflicto(string detalle) => new(EstadoResultado.Conflicto, null, detalle);

    public static Result Prohibido(string detalle) => new(EstadoResultado.Prohibido, null, detalle);
}

/// <summary>
/// <see cref="Result"/> que transporta un valor en caso de éxito. Implementa <see cref="IResultadoInvalidable{TSelf}"/>
/// para que el <c>ValidationBehavior</c> pueda cortocircuitar con un inválido sin conocer <typeparamref name="T"/>.
/// </summary>
public sealed class Result<T> : Result, IResultadoInvalidable<Result<T>>
{
    /// <summary>Valor presente solo cuando <see cref="Result.EsExitoso"/> es verdadero.</summary>
    public T? Valor { get; }

    private Result(T valor)
        : base(EstadoResultado.Ok, null, null) => Valor = valor;

    private Result(EstadoResultado estado, IReadOnlyDictionary<string, string[]>? errores, string? detalle)
        : base(estado, errores, detalle) => Valor = default;

    public static Result<T> Ok(T valor) => new(valor);

    public static new Result<T> Invalido(IReadOnlyDictionary<string, string[]> errores) =>
        new(EstadoResultado.Invalido, errores, null);

    public static new Result<T> NoEncontrado(string detalle) =>
        new(EstadoResultado.NoEncontrado, null, detalle);

    public static new Result<T> Conflicto(string detalle) =>
        new(EstadoResultado.Conflicto, null, detalle);

    public static new Result<T> Prohibido(string detalle) =>
        new(EstadoResultado.Prohibido, null, detalle);
}

/// <summary>
/// Fábrica estática de un resultado inválido, expuesta como miembro estático abstracto (C# 11):
/// permite al <c>ValidationBehavior&lt;TRequest, TResponse&gt;</c> construir un <c>TResponse</c> inválido
/// sin reflexión ni conocer el tipo concreto.
/// </summary>
public interface IResultadoInvalidable<TSelf>
    where TSelf : IResultadoInvalidable<TSelf>
{
    static abstract TSelf Invalido(IReadOnlyDictionary<string, string[]> errores);
}
