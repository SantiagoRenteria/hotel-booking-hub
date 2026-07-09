using HotelBookingHub.Comun.Mensajeria;
using Reservas.Infrastructure.Persistencia;

namespace Reservas.Infrastructure.Mensajeria;

/// <summary>
/// Behavior de escritura (SOLO <see cref="ICommand{TResponse}"/>): asigna el <c>MessageId</c> una vez y
/// delega en el <see cref="EjecutorTransaccional"/> la transacción única que confirma dominio + outbox
/// atómicamente (ADR-018), con retry 1205 y traducción 2627→409. Es el eslabón más interno del pipeline
/// (Logging → Validation → Transaction → Handler); las queries no lo cierran (no son <c>ICommand</c>).
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(EjecutorTransaccional ejecutor, ContextoMensajeria contexto)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, SiguienteEnPipeline<TResponse> siguiente, CancellationToken ct)
    {
        // Antes del retry: si se regenerara dentro del reintento 1205, el UNIQUE(MessageId) no dedupearía.
        contexto.MessageId = Guid.CreateVersion7();

        TResponse respuesta = default!;
        await ejecutor.EjecutarAsync(
            async token =>
            {
                // Reset por intento: el retry re-ejecuta el handler (re-encola), así que el conteo de
                // eventos del guard "1 evento por comando" debe partir de cero en cada intento.
                contexto.ReiniciarConteo();
                respuesta = await siguiente(token);
            },
            ct);
        return respuesta;
    }
}
