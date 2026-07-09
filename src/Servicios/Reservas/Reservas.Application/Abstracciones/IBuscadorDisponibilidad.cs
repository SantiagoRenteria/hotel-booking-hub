using Reservas.Application.Reservas.BuscarDisponibilidad;

namespace Reservas.Application.Abstracciones;

/// <summary>
/// Puerto de lectura del read-model de disponibilidad (Story 3.2). La implementación consulta la
/// <c>ProyeccionHabitacion</c> (catálogo) cruzada con los slots ocupados locales; una decoradora añade la caché
/// Redis (AC-E3.2.3) de forma transparente para el handler.
/// </summary>
public interface IBuscadorDisponibilidad
{
    Task<IReadOnlyList<HabitacionDisponibleDto>> BuscarAsync(BuscarDisponibilidadQuery consulta, CancellationToken ct);
}
