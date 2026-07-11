using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Abstracciones;

namespace Hoteles.Application.Habitaciones.ListarHabitaciones;

/// <summary>
/// Habitaciones de un hotel del agente (Story T.5, AC-ET.5.3). 404 si el hotel no existe/es ajeno/eliminado
/// (no una lista vacía que revele datos ajenos); lista si el hotel es del agente.
/// </summary>
public sealed record ListarHabitacionesDeHotelQuery(Guid HotelId) : IRequest<Result<IReadOnlyList<HabitacionResponseDto>>>;

public sealed class ListarHabitacionesDeHotelQueryHandler(ILectorCatalogo lector, IContextoAgente contexto)
    : IRequestHandler<ListarHabitacionesDeHotelQuery, Result<IReadOnlyList<HabitacionResponseDto>>>
{
    public async Task<Result<IReadOnlyList<HabitacionResponseDto>>> Handle(ListarHabitacionesDeHotelQuery request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contexto.AgenteActual))
        {
            return Result<IReadOnlyList<HabitacionResponseDto>>.Prohibido("Se requiere la identidad del agente.");
        }

        var habitaciones = await lector.ListarHabitacionesDeHotelAsync(request.HotelId, ct);
        return habitaciones is null
            ? Result<IReadOnlyList<HabitacionResponseDto>>.NoEncontrado($"No existe un hotel {request.HotelId} para este agente.")
            : Result<IReadOnlyList<HabitacionResponseDto>>.Ok(habitaciones);
    }
}
