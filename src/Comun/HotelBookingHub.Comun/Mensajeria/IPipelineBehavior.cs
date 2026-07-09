namespace HotelBookingHub.Comun.Mensajeria;

/// <summary>Siguiente eslabón del pipeline (el handler u otro behavior).</summary>
public delegate Task<TResponse> SiguienteEnPipeline<TResponse>(CancellationToken ct);

/// <summary>
/// Behavior del pipeline del mediator (patrón Decorator). Se componen anidados en el orden canónico
/// <c>Logging → Validation → Transaction → Handler</c> (ADR-018); cada uno decide si llama a <paramref name="siguiente"/>.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, SiguienteEnPipeline<TResponse> siguiente, CancellationToken ct);
}
