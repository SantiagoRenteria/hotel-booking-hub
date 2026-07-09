using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Domain.Habitaciones;
using Hoteles.Domain.Puertos;

namespace Hoteles.Application.Habitaciones.CambiarEstadoHabitacion;

/// <summary>
/// Aplica la transición de estado de la habitación (AC-E2.4.3/4). Obtiene (404 si no existe), habilita/
/// deshabilita según <see cref="CambiarEstadoHabitacionCommand.EstadoObjetivo"/> (idempotente) y guarda con
/// concurrencia optimista (409 en conflicto).
/// </summary>
public sealed class CambiarEstadoHabitacionCommandHandler(IHabitacionRepository repositorio)
    : IRequestHandler<CambiarEstadoHabitacionCommand, Result<HabitacionResponseDto>>
{
    public async Task<Result<HabitacionResponseDto>> Handle(CambiarEstadoHabitacionCommand request, CancellationToken ct)
    {
        var habitacion = await repositorio.ObtenerAsync(request.Id, ct);
        if (habitacion is null)
        {
            return Result<HabitacionResponseDto>.NoEncontrado($"No existe una habitación con id {request.Id}.");
        }

        // switch exhaustivo (no if/else): un tercer estado futuro fallaría ruidosamente en vez de degradar.
        switch (request.EstadoObjetivo)
        {
            case EstadoHabitacion.Habilitada:
                habitacion.Habilitar();
                break;
            case EstadoHabitacion.Deshabilitada:
                habitacion.Deshabilitar();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request), request.EstadoObjetivo, "Estado objetivo no soportado.");
        }

        var rowVersion = await repositorio.GuardarConcurrenciaAsync(habitacion, request.RowVersion, ct);

        return Result<HabitacionResponseDto>.Ok(HabitacionResponseDto.De(habitacion, rowVersion));
    }
}
