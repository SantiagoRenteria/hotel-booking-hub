using HotelBookingHub.Comun.Observabilidad;

namespace HotelBookingHub.Comun.Mensajeria.Behaviors;

/// <summary>
/// Behavior de observabilidad: emite UN span de negocio por petición desde el <c>ActivitySource</c> compartido
/// <see cref="ActividadHotelBookingHub"/>, para que la traza tenga un span propio (no solo los de la
/// instrumentación de ASP.NET Core) y el fallo marque el span exacto con su excepción. Sin side effects de
/// negocio (ADR-018); re-lanza como <c>LoggingBehavior</c>. El <c>TraceId</c>/<c>SpanId</c> los propaga la
/// <c>Activity</c> ambient del runtime.
/// </summary>
public sealed class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, SiguienteEnPipeline<TResponse> siguiente, CancellationToken ct)
    {
        using var actividad = ActividadHotelBookingHub.Source.StartActivity(typeof(TRequest).Name);
        try
        {
            return await siguiente(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Solo los fallos reales del servidor marcan Error; una cancelación (aborto de cliente/shutdown) NO
            // es un fallo del span (coherente con DespachadorNotificaciones, que también excluye la cancelación).
            actividad?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            actividad?.AddException(ex);
            throw;
        }
    }
}
