using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Domain.Habitaciones;

namespace Hoteles.Application.Habitaciones.CambiarEstadoHabitacion;

/// <summary>
/// Transición de estado de la habitación (AC-E2.4.3). Un solo handler cubre habilitar/deshabilitar; el
/// <see cref="EstadoObjetivo"/> lo fija el endpoint dedicado (no el cliente). Concurrencia optimista por
/// <see cref="RowVersion"/> (409 en conflicto). Mismo patrón que <c>CambiarEstadoHotel</c> (2.3).
/// </summary>
public sealed record CambiarEstadoHabitacionCommand(
    Guid Id,
    byte[] RowVersion,
    EstadoHabitacion EstadoObjetivo) : ICommand<Result<HabitacionResponseDto>>;
