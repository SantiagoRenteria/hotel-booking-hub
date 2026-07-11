namespace Reservas.Application.Abstracciones;

/// <summary>
/// Registro guardado por una clave de idempotencia: la <b>huella</b> del cuerpo del request (para detectar
/// reutilización de la clave con otro cuerpo) y la <b>respuesta</b> serializada para reproducirla en el reintento.
/// </summary>
public sealed record RegistroIdempotencia(string HuellaCuerpo, string RespuestaJson);

/// <summary>
/// Almacén de idempotencia de creación de reserva (Story 1.7, DOCUMENTO-BASE §8.5). Deduplica el doble-envío del
/// cliente en <c>POST /api/v1/reservas</c> por su header <c>Idempotency-Key</c>. Distinto del <c>MessageId</c> del
/// outbox (interno): esta clave es del cliente. Adaptador Redis (SETNX+TTL) si está configurado; en memoria si no.
/// </summary>
public interface IAlmacenIdempotenciaReserva
{
    /// <summary>Devuelve el registro guardado para la clave, o <c>null</c> si no existe.</summary>
    Task<RegistroIdempotencia?> ObtenerAsync(string clave, CancellationToken ct);

    /// <summary>
    /// Guarda el registro SOLO si la clave aún no existe (semántica SETNX). Devuelve <c>true</c> si se guardó,
    /// <c>false</c> si ya había un registro (otra ejecución ganó la carrera).
    /// </summary>
    Task<bool> GuardarSiAusenteAsync(string clave, RegistroIdempotencia registro, CancellationToken ct);
}
