using System.Text.Json;
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

/// <summary>
/// Publisher espía y SELECTIVO: falla de forma determinista para un par (aggregateId, version) concreto y
/// captura, en orden, los eventos que sí publica. Sirve para probar el head-of-line por agregado del relay
/// (un evento posterior del mismo agregado no debe publicarse antes que uno anterior fallido). Lee el
/// <c>aggregateId</c> del <c>data</c> del envelope (que tras el outbox llega como <see cref="JsonElement"/>).
/// </summary>
public sealed class PublicadorEspiaSelectivo(Func<Guid, int, bool> falla) : IPublicadorEventos
{
    public List<(Guid AggregateId, int Version)> Publicados { get; } = [];

    public Task PublicarAsync(string topico, EventoIntegracion evento, CancellationToken ct)
    {
        var aggregateId = ((JsonElement)evento.Data).GetProperty("aggregateId").GetGuid();
        var version = evento.Version;

        if (falla(aggregateId, version))
        {
            throw new InvalidOperationException($"Fallo selectivo inyectado en ({aggregateId}, v{version}).");
        }

        Publicados.Add((aggregateId, version));
        return Task.CompletedTask;
    }
}
