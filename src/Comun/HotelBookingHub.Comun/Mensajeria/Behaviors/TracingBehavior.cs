using HotelBookingHub.Comun.Observabilidad;

namespace HotelBookingHub.Comun.Mensajeria.Behaviors;

/// <summary>
/// Behavior de observabilidad: emite UN span de negocio por petición desde el <c>ActivitySource</c> compartido
/// <see cref="ActividadHotelBookingHub"/>, para que la traza tenga un span propio (no solo los de la
/// instrumentación de ASP.NET Core) y el fallo marque el span exacto. Sin side effects de negocio (ADR-018);
/// re-lanza como <c>LoggingBehavior</c> (STUB — se implementa en la fase verde).
/// </summary>
public sealed class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> Handle(TRequest request, SiguienteEnPipeline<TResponse> siguiente, CancellationToken ct)
        => siguiente(ct);
}
