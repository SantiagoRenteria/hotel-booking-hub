using System.Diagnostics;
using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Abstracciones;
using Hoteles.Domain.Hoteles;
using Hoteles.Domain.Puertos;

namespace Hoteles.Application.Hoteles.CambiarEstadoHotel;

/// <summary>
/// Aplica la transición de estado (AC-E2.3.1/2/4). Obtiene el hotel activo (404 si no existe o fue eliminado),
/// habilita/deshabilita según <see cref="CambiarEstadoHotelCommand.EstadoObjetivo"/> (idempotente) y guarda con
/// concurrencia optimista (409 en conflicto — AC-E2.3.3). Devuelve el estado y el nuevo <c>rowVersion</c>.
/// <para>
/// Story 3.2 (AC-E3.2.2/FR-7): SOLO la transición EFECTIVA emite un evento de catálogo de hotel
/// (<c>HotelHabilitado</c>/<c>HotelDeshabilitado</c>) por el outbox, en la misma transacción; las idempotentes no.
/// Reservas lo proyecta a una dimensión de estado de hotel y excluye de la búsqueda las habitaciones de un hotel
/// inactivo (sin cascada, preservando el estado individual de cada habitación).
/// </para>
/// </summary>
public sealed class CambiarEstadoHotelCommandHandler(IHotelRepository repositorio, IColaOutbox outbox)
    : IRequestHandler<CambiarEstadoHotelCommand, Result<HotelResponseDto>>
{
    public async Task<Result<HotelResponseDto>> Handle(CambiarEstadoHotelCommand request, CancellationToken ct)
    {
        var hotel = await repositorio.ObtenerAsync(request.Id, ct);
        if (hotel is null)
        {
            return Result<HotelResponseDto>.NoEncontrado($"No existe un hotel activo con id {request.Id}.");
        }

        var traceId = Activity.Current?.TraceId.ToString();

        // switch exhaustivo (no if/else): si algún día se añade un tercer estado, esto falla ruidosamente en vez
        // de degradarlo en silencio a «deshabilitar».
        switch (request.EstadoObjetivo)
        {
            case EstadoHotel.Habilitado:
                if (hotel.Habilitar())
                {
                    outbox.Encolar(
                        HotelHabilitadoV1.Tipo, hotel.Version, hotel.Id,
                        new HotelHabilitadoV1(hotel.Id, hotel.Ciudad), traceId);
                }

                break;
            case EstadoHotel.Deshabilitado:
                if (hotel.Deshabilitar())
                {
                    outbox.Encolar(
                        HotelDeshabilitadoV1.Tipo, hotel.Version, hotel.Id,
                        new HotelDeshabilitadoV1(hotel.Id, hotel.Ciudad), traceId);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request), request.EstadoObjetivo, "Estado objetivo no soportado.");
        }

        var rowVersion = await repositorio.GuardarConcurrenciaAsync(hotel, request.RowVersion, ct);

        return Result<HotelResponseDto>.Ok(new HotelResponseDto(
            hotel.Id, hotel.Nombre, hotel.Ciudad, hotel.Estado.ToString(), Convert.ToBase64String(rowVersion)));
    }
}
