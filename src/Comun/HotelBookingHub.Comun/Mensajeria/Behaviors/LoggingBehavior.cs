using Microsoft.Extensions.Logging;

namespace HotelBookingHub.Comun.Mensajeria.Behaviors;

/// <summary>
/// Primer behavior del pipeline: abre un scope de log/correlación alrededor de la petición.
/// Sin side effects de negocio (ADR-018). El <c>TraceId</c>/<c>SpanId</c> los aporta el enricher de OTel.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, SiguienteEnPipeline<TResponse> siguiente, CancellationToken ct)
    {
        var peticion = typeof(TRequest).Name;
        logger.LogInformation("Procesando {Peticion}", peticion);
        try
        {
            var respuesta = await siguiente(ct);
            logger.LogInformation("Completada {Peticion}", peticion);
            return respuesta;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fallo inesperado en {Peticion}", peticion);
            throw;
        }
    }
}
