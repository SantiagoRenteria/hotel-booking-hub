using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.BuscarDisponibilidad;
using Reservas.Infrastructure.Persistencia;

namespace Reservas.Infrastructure.Proyeccion;

/// <summary>
/// Lectura real del read-model (Story 3.2): consulta la <c>ProyeccionHabitacion</c> (catálogo) cruzada con los
/// slots ocupados de <c>NochesHabitacion</c>. STUB de la fase RED — la lógica de filtro llega en GREEN.
/// </summary>
public sealed class BuscadorDisponibilidadSql(ReservasDbContext db) : IBuscadorDisponibilidad
{
    public Task<IReadOnlyList<HabitacionDisponibleDto>> BuscarAsync(BuscarDisponibilidadQuery consulta, CancellationToken ct) =>
        throw new NotImplementedException(
            $"RED: filtro de disponibilidad pendiente (GREEN). proveedor={db.Database.ProviderName}, ciudad={consulta.Ciudad}");
}
