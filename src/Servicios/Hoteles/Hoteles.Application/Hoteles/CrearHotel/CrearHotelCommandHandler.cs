using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Domain.Hoteles;
using Hoteles.Domain.Puertos;

namespace Hoteles.Application.Hoteles.CrearHotel;

/// <summary>
/// Crea el hotel y lo persiste (AC-E2.1.1). La validación de entrada ya ocurrió en el <c>ValidationBehavior</c>.
/// En 2.1 no se emiten eventos (eso es 2.5), por lo que la escritura es un INSERT auto-contenido.
/// </summary>
public sealed class CrearHotelCommandHandler(IHotelRepository repositorio)
    : IRequestHandler<CrearHotelCommand, Result<HotelResponseDto>>
{
    public async Task<Result<HotelResponseDto>> Handle(CrearHotelCommand request, CancellationToken ct)
    {
        var hotel = Hotel.Crear(request.Nombre, request.Ciudad, request.Direccion, request.Descripcion, request.Estado);
        var rowVersion = await repositorio.CrearAsync(hotel, ct);
        return Result<HotelResponseDto>.Ok(new HotelResponseDto(
            hotel.Id, hotel.Nombre, hotel.Ciudad, hotel.Estado.ToString(), Convert.ToBase64String(rowVersion)));
    }
}
