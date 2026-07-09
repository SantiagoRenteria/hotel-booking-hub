using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Abstracciones;

namespace Reservas.Application.Reservas.ObtenerReservaDetalle;

/// <summary>
/// Detalle de una reserva del agente actual (AC-E3.3.1). <b>Fail-closed:</b> sin identidad → 403. El lector filtra
/// por agente, de modo que una reserva ajena (o inexistente) devuelve null → <b>404</b> (no se filtra contenido ajeno).
/// </summary>
public sealed class ObtenerReservaDetalleQueryHandler(ILectorReservasAgente lector, IContextoAgente contexto)
    : IRequestHandler<ObtenerReservaDetalleQuery, Result<ReservaDetalleDto>>
{
    public async Task<Result<ReservaDetalleDto>> Handle(ObtenerReservaDetalleQuery request, CancellationToken ct)
    {
        var agente = contexto.AgenteActual;
        if (string.IsNullOrWhiteSpace(agente))
        {
            return Result<ReservaDetalleDto>.Prohibido("Se requiere la identidad del agente.");
        }

        var detalle = await lector.ObtenerDetalleAsync(request.ReservaId, agente, ct);
        return detalle is null
            ? Result<ReservaDetalleDto>.NoEncontrado($"No existe una reserva {request.ReservaId} para este agente.")
            : Result<ReservaDetalleDto>.Ok(detalle);
    }
}
