using Microsoft.Extensions.DependencyInjection;

namespace HotelBookingHub.Comun.Mensajeria;

/// <summary>
/// Mediator propio (ADR-018, sin dependencia comercial). Resuelve el handler y los behaviors del
/// contenedor y los compone anidados: <c>behaviors[0]</c> (Logging) queda más externo, el handler al fondo.
/// El costo de reflexión por petición es despreciable frente al I/O de una escritura.
/// </summary>
public sealed class Sender(IServiceProvider proveedor) : ISender
{
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct)
    {
        var tipoRequest = request.GetType();

        var tipoHandler = typeof(IRequestHandler<,>).MakeGenericType(tipoRequest, typeof(TResponse));
        var handler = proveedor.GetRequiredService(tipoHandler);
        var metodoHandle = tipoHandler.GetMethod(nameof(IRequestHandler<IRequest<TResponse>, TResponse>.Handle))!;

        SiguienteEnPipeline<TResponse> pipeline =
            token => (Task<TResponse>)metodoHandle.Invoke(handler, [request, token])!;

        var tipoBehavior = typeof(IPipelineBehavior<,>).MakeGenericType(tipoRequest, typeof(TResponse));
        var metodoBehavior = tipoBehavior.GetMethod(nameof(IPipelineBehavior<IRequest<TResponse>, TResponse>.Handle))!;
        var behaviors = proveedor.GetServices(tipoBehavior).ToList();

        // De adentro hacia afuera: el primero registrado (Logging) envuelve al resto.
        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i]!;
            var siguiente = pipeline;
            pipeline = token => (Task<TResponse>)metodoBehavior.Invoke(behavior, [request, siguiente, token])!;
        }

        return await pipeline(ct);
    }
}
