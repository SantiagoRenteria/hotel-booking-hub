using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Domain.Puertos;

namespace Hoteles.Application.Habitaciones.EditarHabitacion;

/// <summary>
/// Edita una habitación existente (AC-E2.4.2/4). Obtiene (404 si no existe), aplica los cambios de dominio y
/// guarda con concurrencia optimista (409 en conflicto). NO toca el hotel.
/// </summary>
public sealed class EditarHabitacionCommandHandler(IHabitacionRepository repositorio)
    : IRequestHandler<EditarHabitacionCommand, Result<HabitacionResponseDto>>
{
    public async Task<Result<HabitacionResponseDto>> Handle(EditarHabitacionCommand request, CancellationToken ct)
    {
        var habitacion = await repositorio.ObtenerAsync(request.Id, ct);
        if (habitacion is null)
        {
            return Result<HabitacionResponseDto>.NoEncontrado($"No existe una habitación con id {request.Id}.");
        }

        habitacion.Editar(request.Tipo, request.CostoBase, request.Impuestos, request.Ubicacion);
        var rowVersion = await repositorio.GuardarConcurrenciaAsync(habitacion, request.RowVersion, ct);

        return Result<HabitacionResponseDto>.Ok(HabitacionResponseDto.De(habitacion, rowVersion));
    }
}
