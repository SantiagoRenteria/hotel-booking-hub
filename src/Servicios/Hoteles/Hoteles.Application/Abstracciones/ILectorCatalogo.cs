using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Habitaciones;
using Hoteles.Application.Hoteles;

namespace Hoteles.Application.Abstracciones;

/// <summary>
/// Puerto de lectura (CQRS) del catálogo del agente (Story T.5). El aislamiento por agente es server-side: las
/// consultas de hoteles heredan el query filter global de <c>HotelesDbContext</c> (por <c>AgentePropietario</c> +
/// excluye eliminados), y las de habitaciones verifican la propiedad del <b>hotel dueño</b> (la entidad
/// <c>Habitacion</c> no tiene identidad de agente propia). Nunca se materializa el recurso de otro agente.
/// </summary>
public interface ILectorCatalogo
{
    /// <summary>Página de hoteles NO eliminados del agente actual (orden estable por Nombre).</summary>
    Task<PaginaDto<HotelVistaDto>> ListarHotelesAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>Detalle de un hotel del agente; <c>null</c> si no existe, fue eliminado o es de otro agente (→ 404).</summary>
    Task<HotelVistaDto?> ObtenerHotelAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Página de habitaciones de un hotel del agente. <c>null</c> si el hotel no existe/es ajeno/eliminado (→ 404);
    /// página (posiblemente vacía) si el hotel es del agente.
    /// </summary>
    Task<PaginaDto<HabitacionResponseDto>?> ListarHabitacionesDeHotelAsync(Guid hotelId, int page, int pageSize, CancellationToken ct);

    /// <summary>Detalle de una habitación cuyo hotel pertenece al agente; <c>null</c> si no existe o el hotel es ajeno (→ 404).</summary>
    Task<HabitacionResponseDto?> ObtenerHabitacionAsync(Guid id, CancellationToken ct);
}
