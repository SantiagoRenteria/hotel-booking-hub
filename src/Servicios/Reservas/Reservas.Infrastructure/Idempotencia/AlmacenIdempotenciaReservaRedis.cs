using System.Text.Json;
using Reservas.Application.Abstracciones;
using StackExchange.Redis;

namespace Reservas.Infrastructure.Idempotencia;

/// <summary>
/// Almacén de idempotencia sobre Redis (Story 1.7): la reserva usa `StringSet` con `When.NotExists` (SETNX atómico)
/// + TTL — la ÚNICA dedup válida entre instancias del servicio. La finalización sobrescribe el registro con la
/// respuesta; liberar borra la clave. Mismo enfoque que el inbox del worker (5.1b), distinto propósito.
/// </summary>
public sealed class AlmacenIdempotenciaReservaRedis(IConnectionMultiplexer redis, OpcionesIdempotenciaReserva opciones)
    : IAlmacenIdempotenciaReserva
{
    private const string Prefijo = "idem:reserva:";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<bool> ReservarAsync(string clave, string huellaCuerpo, CancellationToken ct)
    {
        var carga = JsonSerializer.Serialize(new RegistroIdempotencia(huellaCuerpo, null), _json);
        return await redis.GetDatabase().StringSetAsync(Prefijo + clave, carga, opciones.Ttl, When.NotExists);
    }

    public async Task<RegistroIdempotencia?> ObtenerAsync(string clave, CancellationToken ct)
    {
        var valor = await redis.GetDatabase().StringGetAsync(Prefijo + clave);
        return valor.HasValue ? JsonSerializer.Deserialize<RegistroIdempotencia>(valor.ToString(), _json) : null;
    }

    public async Task FinalizarAsync(string clave, string huellaCuerpo, string respuestaJson, CancellationToken ct)
    {
        var carga = JsonSerializer.Serialize(new RegistroIdempotencia(huellaCuerpo, respuestaJson), _json);
        // Sobrescribe el marcador "en curso" con la respuesta final, refrescando el TTL.
        await redis.GetDatabase().StringSetAsync(Prefijo + clave, carga, opciones.Ttl);
    }

    public async Task LiberarAsync(string clave, CancellationToken ct) =>
        await redis.GetDatabase().KeyDeleteAsync(Prefijo + clave);
}
