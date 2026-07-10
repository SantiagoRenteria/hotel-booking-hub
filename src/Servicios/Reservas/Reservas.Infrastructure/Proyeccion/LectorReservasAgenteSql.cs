using Microsoft.EntityFrameworkCore;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.ListarReservasDelAgente;
using Reservas.Application.Reservas.ObtenerReservaDetalle;
using Reservas.Infrastructure.Persistencia;

namespace Reservas.Infrastructure.Proyeccion;

/// <summary>
/// Lectura de las reservas del agente (Story 3.3). El aislamiento es server-side: TODA consulta filtra por
/// <c>Reserva.AgenteEmail == agenteEmail</c>, así que nunca se materializa una reserva de otro agente. Cruza con
/// el read-model local <c>ProyeccionHabitacion</c> (LEFT JOIN: la reserva aparece aunque la proyección no esté
/// hidratada) para el hotel/habitación, sin cruzar la frontera a Hoteles. El precio se muestra tal como se
/// persistió (E1), sin recálculo.
/// </summary>
public sealed class LectorReservasAgenteSql(ReservasDbContext db) : ILectorReservasAgente
{
    public async Task<IReadOnlyList<ReservaListadoDto>> ListarAsync(string agenteEmail, CancellationToken ct)
    {
        var filas = await (
                from r in db.Reservas.AsNoTracking()
                where r.AgenteEmail == agenteEmail
                join p in db.ProyeccionesHabitacion.AsNoTracking() on r.HabitacionId equals p.HabitacionId into pj
                from p in pj.DefaultIfEmpty()
                orderby r.Estancia.Entrada, r.Id
                select new
                {
                    r.Id,
                    r.HabitacionId,
                    p!.HotelId,
                    p.Ciudad,
                    p.Tipo,
                    p.Ubicacion,
                    r.Estancia.Entrada,
                    r.Estancia.Salida,
                    r.Estado,
                    r.PrecioTotal,
                })
            .ToListAsync(ct);

        return filas
            .Select(f => new ReservaListadoDto(
                f.Id, f.HabitacionId, f.HotelId, f.Ciudad, f.Tipo, f.Ubicacion,
                f.Entrada, f.Salida, f.Estado.ToString(), f.PrecioTotal))
            .ToList();
    }

    public async Task<ReservaDetalleDto?> ObtenerDetalleAsync(Guid reservaId, string agenteEmail, CancellationToken ct)
    {
        // Filtro por agente EN la consulta: una reserva ajena o inexistente devuelve null (→ 404 en el handler).
        var r = await db.Reservas.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == reservaId && x.AgenteEmail == agenteEmail, ct);
        if (r is null)
        {
            return null;
        }

        var p = await db.ProyeccionesHabitacion.AsNoTracking()
            .FirstOrDefaultAsync(x => x.HabitacionId == r.HabitacionId, ct);

        var listado = new ReservaListadoDto(
            r.Id, r.HabitacionId, p?.HotelId, p?.Ciudad, p?.Tipo, p?.Ubicacion,
            r.Estancia.Entrada, r.Estancia.Salida, r.Estado.ToString(), r.PrecioTotal);

        var huespedes = r.Huespedes
            .Select(h => new HuespedDetalleDto(
                h.Nombres, h.Apellidos, h.FechaNacimiento, h.Genero,
                h.Documento.Tipo, h.Documento.Numero, h.Email, h.Telefono))
            .ToList();

        var contacto = r.ContactoEmergencia is null
            ? null
            : new ContactoEmergenciaDetalleDto(r.ContactoEmergencia.NombreCompleto, r.ContactoEmergencia.Telefono);

        return new ReservaDetalleDto(listado, huespedes, contacto);
    }
}
