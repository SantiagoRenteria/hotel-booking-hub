using FluentValidation;
using FluentValidation.Results;
using HotelBookingHub.Comun.Resultados;

namespace HotelBookingHub.Comun.Mensajeria.Behaviors;

/// <summary>
/// Corta con <c>Result.Invalido</c> (→ 400) ANTES de tocar la BD si algún <see cref="IValidator{T}"/> de
/// la petición falla. No accede a <c>DbContext</c> (ADR-018). Aplica solo a respuestas que sean un
/// <see cref="Result"/> (restricción <see cref="IResultadoInvalidable{TSelf}"/>): las queries que no lo sean
/// no cierran este behavior y lo omiten.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validadores)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IResultadoInvalidable<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, SiguienteEnPipeline<TResponse> siguiente, CancellationToken ct)
    {
        if (!validadores.Any())
        {
            return await siguiente(ct);
        }

        var contexto = new ValidationContext<TRequest>(request);
        var fallas = new List<ValidationFailure>();
        foreach (var validador in validadores)
        {
            var resultado = await validador.ValidateAsync(contexto, ct);
            if (!resultado.IsValid)
            {
                fallas.AddRange(resultado.Errors);
            }
        }

        if (fallas.Count == 0)
        {
            return await siguiente(ct);
        }

        var errores = fallas
            .GroupBy(f => f.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(f => f.ErrorMessage).Distinct().ToArray());

        return TResponse.Invalido(errores);
    }
}
