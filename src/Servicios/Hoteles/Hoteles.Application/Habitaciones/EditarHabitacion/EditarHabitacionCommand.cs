using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;

namespace Hoteles.Application.Habitaciones.EditarHabitacion;

/// <summary>
/// Comando para editar los datos de una habitación (AC-E2.4.2). NO cambia el estado (transición dedicada) ni el
/// hotel. Transporta el <see cref="RowVersion"/> del cliente para la concurrencia optimista (409 en conflicto).
/// </summary>
public sealed record EditarHabitacionCommand(
    Guid Id,
    byte[] RowVersion,
    string Tipo,
    decimal CostoBase,
    decimal Impuestos,
    string Ubicacion) : ICommand<Result<HabitacionResponseDto>>;
