using System.Security.Cryptography;
using System.Text.Json;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Web;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.CrearReserva;

namespace Reservas.Api;

/// <summary>
/// Manejo idempotente de <c>POST /api/v1/reservas</c> (Story 1.7, DOCUMENTO-BASE §8.5). Si el request trae el
/// header <c>Idempotency-Key</c>, un reintento con la MISMA clave y el MISMO cuerpo devuelve la reserva ya creada
/// (sin volver a despachar el comando); la misma clave con cuerpo distinto → 422. Sin header: comportamiento
/// actual intacto. La clave es del CLIENTE (header), distinta del <c>MessageId</c> del outbox (interno).
/// </summary>
internal static class CrearReservaIdempotente
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static async Task<IResult> ManejarAsync(
        CrearReservaCommand comando,
        string? idempotencyKey,
        ISender sender,
        IAlmacenIdempotenciaReserva idempotencia,
        CancellationToken ct)
    {
        // Sin clave de idempotencia → flujo actual sin cambios (AC-E1.7.4).
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var sinClave = await sender.Send(comando, ct);
            return sinClave.ToCreatedResult(dto => $"/api/v1/reservas/{dto.Id}");
        }

        var huella = Huella(comando);
        var existente = await idempotencia.ObtenerAsync(idempotencyKey, ct);
        if (existente is not null)
        {
            // Misma clave, cuerpo distinto → 422 (una clave de idempotencia no se reutiliza para otra operación).
            if (existente.HuellaCuerpo != huella)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Idempotency-Key reutilizada",
                    detail: "La clave de idempotencia ya se usó con un cuerpo distinto.");
            }

            // Misma clave, mismo cuerpo → replay de la reserva ya creada (200), sin re-despachar el comando.
            var guardada = JsonSerializer.Deserialize<ReservaResponseDto>(existente.RespuestaJson, _json)!;
            return TypedResults.Ok(guardada);
        }

        var resultado = await sender.Send(comando, ct);
        // Solo se fija la clave ante una creación EXITOSA: un 400/409 no debe "quemar" la clave (permite reintento).
        if (resultado.EsExitoso)
        {
            var registro = new RegistroIdempotencia(huella, JsonSerializer.Serialize(resultado.Valor, _json));
            await idempotencia.GuardarSiAusenteAsync(idempotencyKey, registro, ct);
        }

        return resultado.ToCreatedResult(dto => $"/api/v1/reservas/{dto.Id}");
    }

    /// <summary>Huella determinista del cuerpo (SHA-256 del JSON del comando) para detectar reutilización de clave.</summary>
    private static string Huella(CrearReservaCommand comando) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(comando, _json)));
}
