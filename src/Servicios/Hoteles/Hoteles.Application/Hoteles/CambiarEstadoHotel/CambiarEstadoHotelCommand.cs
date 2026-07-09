using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Domain.Hoteles;

namespace Hoteles.Application.Hoteles.CambiarEstadoHotel;

/// <summary>
/// Comando de transición del ciclo de vida del hotel (AC-E2.3.1). Un solo handler cubre habilitar y deshabilitar;
/// el <see cref="EstadoObjetivo"/> lo fija el ENDPOINT dedicado (<c>:habilitar</c>/<c>:deshabilitar</c>), no el
/// cliente — así no hay un set de estado arbitrario (el PUT de editar tampoco lo toca). Transporta el
/// <see cref="RowVersion"/> del cliente para la concurrencia optimista (409 en conflicto, como 2.2).
/// </summary>
public sealed record CambiarEstadoHotelCommand(
    Guid Id,
    byte[] RowVersion,
    EstadoHotel EstadoObjetivo) : ICommand<Result<HotelResponseDto>>;
