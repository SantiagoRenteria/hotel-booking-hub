using System.Security.Cryptography;
using System.Text.Json;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Web;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.CrearReserva;

namespace Reservas.Api;

/// <summary>
/// Manejo idempotente de <c>POST /api/v1/reservas</c> (Story 1.7, DOCUMENTO-BASE §8.5). Protocolo
/// <b>reservar-antes-de-despachar</b>: se reserva la clave (SETNX de un marcador "en curso") ANTES de despachar el
/// comando, de modo que dos requests concurrentes con la misma clave NO creen dos reservas — solo el que gana la
/// reserva despacha; el otro obtiene el replay (si ya terminó) o 409 (si sigue en curso). La clave del CLIENTE
/// (header) se scopea por el sujeto autenticado para no cruzar reservas entre usuarios; es distinta del
/// <c>MessageId</c> del outbox (interno). Sin header: comportamiento actual intacto.
/// </summary>
internal static class CrearReservaIdempotente
{
    private const int MaxLongitudClave = 200;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static async Task<IResult> ManejarAsync(
        CrearReservaCommand comando,
        string? idempotencyKey,
        ISender sender,
        IAlmacenIdempotenciaReserva idempotencia,
        IContextoAgente contexto,
        CancellationToken ct)
    {
        // Sin clave de idempotencia → flujo actual sin cambios (AC-E1.7.4).
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var sinClave = await sender.Send(comando, ct);
            return sinClave.ToCreatedResult(dto => $"/api/v1/reservas/{dto.Id}");
        }

        // Clave del cliente acotada en tamaño (evita una key de Redis arbitrariamente grande, vector de DoS).
        if (idempotencyKey.Length > MaxLongitudClave)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Idempotency-Key inválida",
                detail: $"La clave de idempotencia excede {MaxLongitudClave} caracteres.");
        }

        // Scope por el sujeto autenticado: dos usuarios con la misma clave NO comparten ni bloquean reservas.
        var clave = $"{contexto.AgenteActual ?? "anon"}:{idempotencyKey}";
        var huella = Huella(comando);

        // Reservar-antes-de-despachar: solo el que gana el SETNX despacha el comando.
        var reservado = await idempotencia.ReservarAsync(clave, huella, ct);
        if (!reservado)
        {
            return await ResolverExistenteAsync(idempotencia, clave, huella, ct);
        }

        try
        {
            var resultado = await sender.Send(comando, ct);
            if (resultado.EsExitoso)
            {
                await idempotencia.FinalizarAsync(clave, huella, JsonSerializer.Serialize(resultado.Valor, _json), ct);
            }
            else
            {
                // 400/409 no "queman" la clave: se libera para permitir un reintento legítimo.
                await idempotencia.LiberarAsync(clave, ct);
            }

            return resultado.ToCreatedResult(dto => $"/api/v1/reservas/{dto.Id}");
        }
        catch
        {
            // Excepción inesperada tras reservar: liberar la clave para no dejarla "en curso" para siempre.
            await idempotencia.LiberarAsync(clave, ct);
            throw;
        }
    }

    /// <summary>La clave ya existía: replay si terminó y coincide el cuerpo; 422 si el cuerpo difiere; 409 si sigue en curso.</summary>
    private static async Task<IResult> ResolverExistenteAsync(
        IAlmacenIdempotenciaReserva idempotencia, string clave, string huella, CancellationToken ct)
    {
        var existente = await idempotencia.ObtenerAsync(clave, ct);
        if (existente is null)
        {
            // Carrera rara (la clave expiró/se liberó entre reservar y leer): pedir reintento sin arriesgar doble creación.
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "Reintente", detail: "No se pudo resolver la idempotencia; reintente.");
        }

        // Misma clave, cuerpo distinto → 422 (una clave de idempotencia no se reutiliza para otra operación).
        if (existente.HuellaCuerpo != huella)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Idempotency-Key reutilizada",
                detail: "La clave de idempotencia ya se usó con un cuerpo distinto.");
        }

        // Reservada pero aún sin respuesta → otra ejecución la está procesando (concurrencia): 409, reintente.
        if (existente.RespuestaJson is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Reserva en proceso",
                detail: "Una petición con la misma Idempotency-Key se está procesando; reintente en unos instantes.");
        }

        // Mismo cuerpo y respuesta guardada → replay (200) de la MISMA reserva, sin re-despachar el comando.
        var guardada = JsonSerializer.Deserialize<ReservaResponseDto>(existente.RespuestaJson, _json);
        if (guardada is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "Reintente", detail: "Respuesta idempotente no recuperable; reintente.");
        }

        return TypedResults.Ok(guardada);
    }

    /// <summary>Huella determinista del cuerpo (SHA-256 del JSON del comando) para detectar reutilización de clave.</summary>
    private static string Huella(CrearReservaCommand comando) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(comando, _json)));
}
