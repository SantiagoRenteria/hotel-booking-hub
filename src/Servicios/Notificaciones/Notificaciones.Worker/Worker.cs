namespace Notificaciones.Worker;

/// <summary>
/// Worker de notificaciones. En el esqueleto (Story 1.1) solo late para evidenciar que el host
/// corre; la suscripción idempotente a eventos de dominio (ReservaConfirmada, cancelaciones) y el
/// envío por SMTP se implementan en la Épica 5.
/// </summary>
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Worker de notificaciones activo: {momento}", DateTimeOffset.UtcNow);
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
