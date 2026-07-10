using Reservas.Application.Reservas.ListarReservasDelAgente;
using Reservas.Application.Reservas.ObtenerReservaDetalle;

namespace Reservas.Application.Abstracciones;

/// <summary>
/// Puerto de lectura (CQRS) de las reservas del agente (Story 3.3). El aislamiento por <paramref name="agenteEmail"/>
/// se aplica DENTRO de la consulta (server-side): nunca se devuelven reservas de otro agente. Cruza la reserva con
/// el read-model de catálogo (<c>ProyeccionHabitacion</c>) para el hotel/habitación, sin llamar a Hoteles.
/// </summary>
public interface ILectorReservasAgente
{
    /// <summary>Listado de las reservas del agente (AC-E3.3.1/2): hotel, habitación, estancia, estado, precio.</summary>
    Task<IReadOnlyList<ReservaListadoDto>> ListarAsync(string agenteEmail, CancellationToken ct);

    /// <summary>Detalle de UNA reserva del agente (AC-E3.3.1): añade huéspedes + contacto. Null si no es suya o no existe.</summary>
    Task<ReservaDetalleDto?> ObtenerDetalleAsync(Guid reservaId, string agenteEmail, CancellationToken ct);
}
