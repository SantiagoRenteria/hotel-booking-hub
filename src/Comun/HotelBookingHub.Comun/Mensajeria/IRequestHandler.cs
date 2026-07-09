namespace HotelBookingHub.Comun.Mensajeria;

/// <summary>
/// Handler de una petición. Firma del contrato del mediator (ADR-018):
/// <c>Task&lt;TResponse&gt; Handle(TRequest, CancellationToken)</c>; los flujos esperados no lanzan.
/// </summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}
