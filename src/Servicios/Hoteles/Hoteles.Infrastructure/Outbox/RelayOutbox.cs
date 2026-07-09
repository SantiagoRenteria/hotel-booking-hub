using Hoteles.Infrastructure.Persistencia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hoteles.Infrastructure.Outbox;

/// <summary>
/// Relay del productor de catálogo: sondea el outbox de Hoteles y publica los pendientes vía
/// <c>IPublicadorEventos</c> (log en dev; Dapr pub/sub en producción). Crea un scope por ciclo (el
/// <c>DbContext</c> es scoped). Un fallo del ciclo no tumba el servicio: se registra y se reintenta al siguiente sondeo.
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
                var db = scope.ServiceProvider.GetRequiredService<HotelesDbContext>();
                await procesador.ProcesarLoteAsync(db, DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fallo en el ciclo del relay de outbox de Hoteles");
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
