using HotelBookingHub.Comun.Eventos;

namespace Hoteles.IntegrationTests;

/// <summary>Publisher fake que captura los eventos publicados (no depende de Dapr ni broker).</summary>
public sealed class PublicadorEventosFake : IPublicadorEventos
{
    public List<EventoIntegracion> Publicados { get; } = [];

    public Task PublicarAsync(string topico, EventoIntegracion evento, CancellationToken ct)
    {
        Publicados.Add(evento);
        return Task.CompletedTask;
    }
}

/// <summary>Publisher que siempre falla: simula el broker caído para probar el no-mark-sent prematuro.</summary>
public sealed class PublicadorEventosQueFalla : IPublicadorEventos
{
    public int Intentos { get; private set; }

    public Task PublicarAsync(string topico, EventoIntegracion evento, CancellationToken ct)
    {
        Intentos++;
        throw new InvalidOperationException("Broker caído (simulado).");
    }
}
