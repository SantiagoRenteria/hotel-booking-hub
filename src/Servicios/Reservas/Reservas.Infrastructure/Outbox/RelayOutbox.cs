using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Reservas.Infrastructure.Persistencia;

namespace Reservas.Infrastructure.Outbox;

/// <summary>
/// Relay del productor: sondea el outbox y publica los pendientes vía <c>IPublicadorEventos</c> (fake en dev;
/// Dapr pub/sub en producción). Crea un scope por ciclo (el <c>DbContext</c> es scoped). Un fallo del ciclo
/// no tumba el servicio: se registra y se reintenta en el siguiente sondeo.
/// </summary>
public sealed class RelayOutbox(IServiceScopeFactory scopeFactory, ProcesadorOutbox procesador, OpcionesRelayOutbox opciones, ILogger<RelayOutbox> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ReservasDbContext>();
                await procesador.ProcesarLoteAsync(db, DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fallo en el ciclo del relay de outbox");
            }

            try
            {
                await Task.Delay(opciones.Intervalo, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
