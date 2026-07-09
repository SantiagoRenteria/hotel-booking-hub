using Hoteles.Application.Abstracciones;

namespace Hoteles.UnitTests;

/// <summary>
/// Fake de <see cref="IColaOutbox"/> sin BD: captura lo encolado por los handlers para afirmar QUÉ evento se
/// emite, con qué <c>version</c> (order key) y con qué datos — y, sobre todo, CUÁNDO NO se emite (selectividad
/// AC-E2.5.3). No persiste nada: la atomicidad real (una sola tx) se prueba en integración con Testcontainers.
/// </summary>
public sealed class ColaOutboxFake : IColaOutbox
{
    public sealed record Encolado(string Tipo, int Version, Guid AggregateId, object Data, string? TraceId);

    public List<Encolado> Encolados { get; } = [];

    public void Encolar(string tipo, int version, Guid aggregateId, object data, string? traceId) =>
        Encolados.Add(new Encolado(tipo, version, aggregateId, data, traceId));
}
