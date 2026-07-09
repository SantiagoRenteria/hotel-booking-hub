using System.Diagnostics;
using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Abstracciones;
using Hoteles.Domain.Puertos;

namespace Hoteles.Application.Habitaciones.EditarHabitacion;

/// <summary>
/// Edita una habitación existente (AC-E2.4.2/4). Obtiene (404 si no existe), aplica los cambios de dominio y
/// guarda con concurrencia optimista (409 en conflicto). NO toca el hotel. Emite <c>PrecioHabitacionCambiado</c>
/// SOLO si cambió el precio (AC-E2.5.3), encolado en la misma transacción del guardado (Opción U).
/// </summary>
public sealed class EditarHabitacionCommandHandler(IHabitacionRepository repositorio, IColaOutbox outbox)
    : IRequestHandler<EditarHabitacionCommand, Result<HabitacionResponseDto>>
{
    public async Task<Result<HabitacionResponseDto>> Handle(EditarHabitacionCommand request, CancellationToken ct)
    {
        var habitacion = await repositorio.ObtenerAsync(request.Id, ct);
        if (habitacion is null)
        {
            return Result<HabitacionResponseDto>.NoEncontrado($"No existe una habitación con id {request.Id}.");
        }

        var cambioPrecio = habitacion.Editar(request.Tipo, request.CostoBase, request.Impuestos, request.Ubicacion);
        if (cambioPrecio)
        {
            var data = new PrecioHabitacionCambiadoV1(
                habitacion.Id, habitacion.HotelId, habitacion.CostoBase, habitacion.Impuestos);
            outbox.Encolar(PrecioHabitacionCambiadoV1.Tipo, habitacion.Version, habitacion.Id, data,
                Activity.Current?.TraceId.ToString());
        }

        var rowVersion = await repositorio.GuardarConcurrenciaAsync(habitacion, request.RowVersion, ct);

        return Result<HabitacionResponseDto>.Ok(HabitacionResponseDto.De(habitacion, rowVersion));
    }
}
