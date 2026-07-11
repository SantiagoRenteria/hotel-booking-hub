using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Abstracciones;

namespace Hoteles.Application.Hoteles.ObtenerHotel;

/// <summary>Detalle de un hotel del agente (Story T.5, AC-ET.5.2). 404 si no existe, eliminado o ajeno.</summary>
public sealed record ObtenerHotelDetalleQuery(Guid Id) : IRequest<Result<HotelVistaDto>>;

public sealed class ObtenerHotelDetalleQueryHandler(ILectorCatalogo lector)
    : IRequestHandler<ObtenerHotelDetalleQuery, Result<HotelVistaDto>>
{
    public async Task<Result<HotelVistaDto>> Handle(ObtenerHotelDetalleQuery request, CancellationToken ct)
    {
        var vista = await lector.ObtenerHotelAsync(request.Id, ct);
        return vista is null
            ? Result<HotelVistaDto>.NoEncontrado($"No existe un hotel {request.Id} para este agente.")
            : Result<HotelVistaDto>.Ok(vista);
    }
}
