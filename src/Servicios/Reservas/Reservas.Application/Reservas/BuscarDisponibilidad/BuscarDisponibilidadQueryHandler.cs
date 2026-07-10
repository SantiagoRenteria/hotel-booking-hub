using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Abstracciones;

namespace Reservas.Application.Reservas.BuscarDisponibilidad;

/// <summary>
/// Handler de la búsqueda de disponibilidad (AC-E3.2.1/2/3). Delega en el <see cref="IBuscadorDisponibilidad"/>
/// (cuya decoradora resuelve la caché) y envuelve el resultado en un <see cref="Result{T}"/> Ok. La validación de
/// entrada ya ocurrió en el pipeline; una búsqueda sin coincidencias es un 200 con lista vacía, no un 404.
/// </summary>
public sealed class BuscarDisponibilidadQueryHandler(IBuscadorDisponibilidad buscador)
    : IRequestHandler<BuscarDisponibilidadQuery, Result<IReadOnlyList<HabitacionDisponibleDto>>>
{
    public async Task<Result<IReadOnlyList<HabitacionDisponibleDto>>> Handle(
        BuscarDisponibilidadQuery request, CancellationToken ct)
    {
        var disponibles = await buscador.BuscarAsync(request, ct);
        return Result<IReadOnlyList<HabitacionDisponibleDto>>.Ok(disponibles);
    }
}
