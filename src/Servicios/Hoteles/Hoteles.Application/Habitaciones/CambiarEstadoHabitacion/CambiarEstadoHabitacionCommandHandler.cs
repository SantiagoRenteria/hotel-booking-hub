using System.Diagnostics;
using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Abstracciones;
using Hoteles.Domain.Habitaciones;
using Hoteles.Domain.Puertos;

namespace Hoteles.Application.Habitaciones.CambiarEstadoHabitacion;

/// <summary>
/// Aplica la transición de estado de la habitación (AC-E2.4.3/4). Obtiene (404 si no existe), habilita/
/// deshabilita según <see cref="CambiarEstadoHabitacionCommand.EstadoObjetivo"/> (idempotente) y guarda con
/// concurrencia optimista (409 en conflicto). Emite <c>HabitacionDeshabilitada</c> SOLO cuando la habitación
/// pasa realmente a deshabilitada (AC-E2.5.3: habilitar e idempotentes NO emiten), en la misma transacción.
/// </summary>
public sealed class CambiarEstadoHabitacionCommandHandler(
    IHabitacionRepository repositorio, IHotelRepository hoteles, IColaOutbox outbox)
    : IRequestHandler<CambiarEstadoHabitacionCommand, Result<HabitacionResponseDto>>
{
    public async Task<Result<HabitacionResponseDto>> Handle(CambiarEstadoHabitacionCommand request, CancellationToken ct)
    {
        var habitacion = await repositorio.ObtenerAsync(request.Id, ct);
        if (habitacion is null)
        {
            return Result<HabitacionResponseDto>.NoEncontrado($"No existe una habitación con id {request.Id}.");
        }

        // Aislamiento (Story 6.3): la habitación se aísla por el propietario de su hotel (query filter por
        // propietario al cargar el hotel padre → ajeno/eliminado invisible → 404).
        if (await hoteles.ObtenerAsync(habitacion.HotelId, ct) is null)
        {
            return Result<HabitacionResponseDto>.NoEncontrado($"No existe una habitación con id {request.Id}.");
        }

        // switch exhaustivo (no if/else): un tercer estado futuro fallaría ruidosamente en vez de degradar.
        // Solo la deshabilitación EFECTIVA (cambió el estado) emite evento; habilitar no tiene evento de catálogo.
        switch (request.EstadoObjetivo)
        {
            case EstadoHabitacion.Habilitada:
                habitacion.Habilitar();
                break;
            case EstadoHabitacion.Deshabilitada:
                if (habitacion.Deshabilitar())
                {
                    outbox.Encolar(
                        HabitacionDeshabilitadaV1.Tipo, habitacion.Version, habitacion.Id,
                        new HabitacionDeshabilitadaV1(habitacion.Id, habitacion.HotelId),
                        Activity.Current?.TraceId.ToString());
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request), request.EstadoObjetivo, "Estado objetivo no soportado.");
        }

        var rowVersion = await repositorio.GuardarConcurrenciaAsync(habitacion, request.RowVersion, ct);

        return Result<HabitacionResponseDto>.Ok(HabitacionResponseDto.De(habitacion, rowVersion));
    }
}
