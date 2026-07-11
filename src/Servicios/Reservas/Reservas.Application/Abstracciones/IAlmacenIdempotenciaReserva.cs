namespace Reservas.Application.Abstracciones;

/// <summary>
/// Registro de una clave de idempotencia: la <b>huella</b> del cuerpo (para detectar reutilización con otro
/// cuerpo) y la <b>respuesta</b> serializada. <see cref="RespuestaJson"/> es <c>null</c> mientras la operación
/// está <b>en curso</b> (la clave fue reservada pero el comando aún no terminó).
/// </summary>
public sealed record RegistroIdempotencia(string HuellaCuerpo, string? RespuestaJson);

/// <summary>
/// Almacén de idempotencia de creación de reserva (Story 1.7, DOCUMENTO-BASE §8.5). Protocolo
/// <b>reservar-antes-de-despachar</b>: se RESERVA la clave (marcador "en curso") ANTES de ejecutar el comando, de
/// modo que dos requests concurrentes con la misma clave no puedan crear dos reservas — solo el que gana la
/// reserva despacha. La clave es del CLIENTE (header <c>Idempotency-Key</c>), scopeada por el sujeto autenticado;
/// distinta del <c>MessageId</c> del outbox. Adaptador Redis (SETNX+TTL) entre instancias; memoria si no hay Redis.
/// </summary>
public interface IAlmacenIdempotenciaReserva
{
    /// <summary>
    /// Reserva la clave con un marcador "en curso" (huella + respuesta nula) SOLO si aún no existe (SETNX).
    /// Devuelve <c>true</c> si esta ejecución ganó la reserva (debe despachar); <c>false</c> si ya existía.
    /// </summary>
    Task<bool> ReservarAsync(string clave, string huellaCuerpo, CancellationToken ct);

    /// <summary>Devuelve el registro de la clave (con o sin respuesta), o <c>null</c> si no existe/expiró.</summary>
    Task<RegistroIdempotencia?> ObtenerAsync(string clave, CancellationToken ct);

    /// <summary>Finaliza una clave reservada guardando la respuesta (el comando terminó con éxito).</summary>
    Task FinalizarAsync(string clave, string huellaCuerpo, string respuestaJson, CancellationToken ct);

    /// <summary>Libera una clave reservada (el comando falló: 400/409) para permitir un reintento legítimo.</summary>
    Task LiberarAsync(string clave, CancellationToken ct);
}
