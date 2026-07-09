using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Domain.Puertos;

namespace Hoteles.Application.Hoteles.EditarHotel;

/// <summary>
/// Edita un hotel existente (AC-E2.2.1/4). Obtiene el hotel activo (404 si no existe o fue eliminado), aplica
/// los cambios de dominio y guarda con concurrencia optimista. El conflicto de concurrencia NO es un
/// <see cref="Result"/>: emerge dentro de <c>SaveChanges</c> y se propaga como excepción que el handler
/// transversal traduce a 409 (AC-E2.2.3).
/// </summary>
public sealed class EditarHotelCommandHandler(IHotelRepository repositorio)
    : IRequestHandler<EditarHotelCommand, Result<HotelResponseDto>>
{
    public async Task<Result<HotelResponseDto>> Handle(EditarHotelCommand request, CancellationToken ct)
    {
        var hotel = await repositorio.ObtenerAsync(request.Id, ct);
        if (hotel is null)
        {
            return Result<HotelResponseDto>.NoEncontrado($"No existe un hotel activo con id {request.Id}.");
        }

        hotel.Editar(request.Nombre, request.Ciudad, request.Direccion, request.Descripcion);
        var rowVersion = await repositorio.GuardarConcurrenciaAsync(hotel, request.RowVersion, ct);

        return Result<HotelResponseDto>.Ok(new HotelResponseDto(
            hotel.Id, hotel.Nombre, hotel.Ciudad, hotel.Estado.ToString(), Convert.ToBase64String(rowVersion)));
    }
}
