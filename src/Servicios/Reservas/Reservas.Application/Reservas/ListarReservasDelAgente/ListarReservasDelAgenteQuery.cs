using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;

namespace Reservas.Application.Reservas.ListarReservasDelAgente;

/// <summary>
/// Query de lectura (CQRS) del listado de reservas del agente actual (AC-E3.3.1/2). Es <see cref="IRequest{T}"/>
/// (no <c>ICommand</c>) → no pasa por el <c>TransactionBehavior</c>. NO transporta la identidad del agente: esa se
/// resuelve server-side vía <c>IContextoAgente</c> en el handler (el cliente no puede elegir a qué agente ve).
/// </summary>
public sealed record ListarReservasDelAgenteQuery : IRequest<Result<IReadOnlyList<ReservaListadoDto>>>;

/// <summary>
/// Ítem del listado (AC-E3.3.1): hotel, habitación, estancia, estado y precio. El hotel/habitación provienen del
/// read-model de catálogo (pueden ser null si la proyección aún no está hidratada); el precio se muestra tal como
/// se persistió (E1), sin recálculo. El detalle (huéspedes + contacto) va en <c>ReservaDetalleDto</c>.
/// </summary>
public sealed record ReservaListadoDto(
    Guid Id,
    Guid HabitacionId,
    Guid? HotelId,
    string? Ciudad,
    string? Tipo,
    string? Ubicacion,
    DateOnly Entrada,
    DateOnly Salida,
    string Estado,
    decimal PrecioTotal);
