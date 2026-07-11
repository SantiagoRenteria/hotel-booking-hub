using System.Collections.Concurrent;
using Reservas.Application.Abstracciones;

namespace Reservas.Infrastructure.Idempotencia;

/// <summary>
/// Fallback en memoria del almacén de idempotencia (dev sin Redis). `TryAdd` da la misma semántica SETNX que el
/// adaptador Redis (no sobrescribe una clave existente). NO deduplica entre instancias del servicio — para eso
/// está el adaptador Redis; este es solo para desarrollo local de una sola instancia.
/// </summary>
public sealed class AlmacenIdempotenciaReservaEnMemoria : IAlmacenIdempotenciaReserva
{
    private readonly ConcurrentDictionary<string, RegistroIdempotencia> _mapa = new(StringComparer.Ordinal);

    public Task<RegistroIdempotencia?> ObtenerAsync(string clave, CancellationToken ct) =>
        Task.FromResult(_mapa.TryGetValue(clave, out var registro) ? registro : null);

    public Task<bool> GuardarSiAusenteAsync(string clave, RegistroIdempotencia registro, CancellationToken ct) =>
        Task.FromResult(_mapa.TryAdd(clave, registro));
}
