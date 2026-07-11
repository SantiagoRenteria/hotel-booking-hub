using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Abstracciones;

namespace Hoteles.Application.Habitaciones.ObtenerHabitacion;

/// <summary>Detalle de una habitación cuyo hotel pertenece al agente (Story T.5, AC-ET.5.4). 404 si no existe o el hotel es ajeno.</summary>
public sealed record ObtenerHabitacionDetalleQuery(Guid Id) : IRequest<Result<HabitacionResponseDto>>;

public sealed class ObtenerHabitacionDetalleQueryHandler(ILectorCatalogo lector, IContextoAgente contexto)
    : IRequestHandler<ObtenerHabitacionDetalleQuery, Result<HabitacionResponseDto>>
{
    public async Task<Result<HabitacionResponseDto>> Handle(ObtenerHabitacionDetalleQuery request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contexto.AgenteActual))
        {
            return Result<HabitacionResponseDto>.Prohibido("Se requiere la identidad del agente.");
        }

        var vista = await lector.ObtenerHabitacionAsync(request.Id, ct);
        return vista is null
            ? Result<HabitacionResponseDto>.NoEncontrado($"No existe una habitación {request.Id} para este agente.")
            : Result<HabitacionResponseDto>.Ok(vista);
    }
}
