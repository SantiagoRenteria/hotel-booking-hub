using System.Text.Json;
using Reservas.Application.Abstracciones;
using StackExchange.Redis;

namespace Reservas.Infrastructure.Idempotencia;

/// <summary>
/// Almacén de idempotencia sobre Redis (Story 1.7): `StringSet` con `When.NotExists` (SETNX atómico) + TTL — la
/// ÚNICA dedup válida entre instancias del servicio. Mismo enfoque que el inbox del worker (5.1b), distinto
/// propósito (dedup del `POST` del cliente, no del efecto del consumidor).
/// </summary>
public sealed class AlmacenIdempotenciaReservaRedis(IConnectionMultiplexer redis, OpcionesIdempotenciaReserva opciones)
    : IAlmacenIdempotenciaReserva
{
    private const string Prefijo = "idem:reserva:";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<RegistroIdempotencia?> ObtenerAsync(string clave, CancellationToken ct)
    {
        var valor = await redis.GetDatabase().StringGetAsync(Prefijo + clave);
        return valor.HasValue ? JsonSerializer.Deserialize<RegistroIdempotencia>(valor.ToString(), _json) : null;
    }

    public async Task<bool> GuardarSiAusenteAsync(string clave, RegistroIdempotencia registro, CancellationToken ct)
    {
        var carga = JsonSerializer.Serialize(registro, _json);
        return await redis.GetDatabase().StringSetAsync(Prefijo + clave, carga, opciones.Ttl, When.NotExists);
    }
}
