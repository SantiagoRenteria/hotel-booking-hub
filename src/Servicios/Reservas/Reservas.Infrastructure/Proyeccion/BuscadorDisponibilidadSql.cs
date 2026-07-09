using Microsoft.EntityFrameworkCore;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.BuscarDisponibilidad;
using Reservas.Infrastructure.Persistencia;

namespace Reservas.Infrastructure.Proyeccion;

/// <summary>
/// Lectura del read-model de disponibilidad (Story 3.2, AC-E3.2.1/2). Consulta la <c>ProyeccionHabitacion</c>
/// (catálogo local, alimentado por eventos de Hoteles) cruzada con los slots ocupados de <c>NochesHabitacion</c>
/// (el mismo BC de Reservas): filtra por ciudad, fila hidratada, estado activo, capacidad suficiente y ausencia
/// de solapamiento con las noches del rango semiabierto <c>[entrada, salida)</c>. Nunca lee del catálogo de
/// Hoteles (no cruza la frontera del BC). Best-effort: a lo sumo un 409 evitable, nunca overbooking.
/// </summary>
public sealed class BuscadorDisponibilidadSql(ReservasDbContext db) : IBuscadorDisponibilidad
{
    // Estado individual de la habitación en la proyección. El estado del HOTEL es una dimensión aparte
    // (ProyeccionHotelEstado) que se cruza en la query: son ciclos de vida independientes (field-ownership).
    private const string Activa = "Habilitada";

    public async Task<IReadOnlyList<HabitacionDisponibleDto>> BuscarAsync(BuscarDisponibilidadQuery consulta, CancellationToken ct)
    {
        var (ciudad, entrada, salida, huespedes) = consulta;

        // Defensa en profundidad: el ValidationBehavior ya rechaza esto en el pipeline, pero un consumidor directo
        // del puerto con rango degenerado/huéspedes no positivos NO debe reportar todo el inventario como libre.
        if (salida <= entrada || huespedes < 1)
        {
            return [];
        }

        // Normalización consistente con la clave de caché e invalidación (Trim + minúsculas): evita que variantes
        // por espacios/mayúsculas produzcan resultados incoherentes entre SQL y caché.
        var ciudadNorm = ciudad.Trim().ToLowerInvariant();

        var candidatas = db.ProyeccionesHabitacion.AsNoTracking()
            .Where(p => p.Ciudad!.ToLower() == ciudadNorm
                && p.VersionEstatico != null            // hidratada: ya llegó el alta (estáticos presentes)
                && p.Estado == Activa                   // habitación activa (no deshabilitada individualmente)
                && p.Capacidad >= huespedes             // capacidad suficiente
                && !db.ProyeccionesHotelEstado.Any(h => // hotel activo (dimensión independiente, AC-E3.2.2/FR-7)
                        h.HotelId == p.HotelId && !h.Activo)
                && !db.NochesHabitacion.Any(n =>        // todas las noches de [entrada, salida) libres
                        n.HabitacionId == p.HabitacionId && n.Noche >= entrada && n.Noche < salida))
            .OrderBy(p => p.CostoBase).ThenBy(p => p.HabitacionId)
            .Select(p => new
            {
                p.HabitacionId, p.HotelId, p.Ciudad, p.Tipo, p.Ubicacion, p.Capacidad, p.CostoBase, p.Impuestos,
            });

        var filas = await candidatas.ToListAsync(ct);

        // El filtro por hidratada garantiza que los estáticos y el precio inicial ya están presentes.
        return filas
            .Select(f => new HabitacionDisponibleDto(
                f.HabitacionId, f.HotelId!.Value, f.Ciudad!, f.Tipo!, f.Ubicacion!,
                f.Capacidad!.Value, f.CostoBase!.Value, f.Impuestos ?? 0m))
            .ToList();
    }
}
