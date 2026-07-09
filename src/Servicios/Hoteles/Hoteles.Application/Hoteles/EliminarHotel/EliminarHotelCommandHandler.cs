using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Domain.Puertos;

namespace Hoteles.Application.Hoteles.EliminarHotel;

/// <summary>
/// Da de baja lógica un hotel (AC-E2.2.2/4). Obtiene el hotel activo (404 si no existe o ya fue eliminado),
/// lo marca como eliminado y guarda con concurrencia optimista (409 si pierde una carrera — AC-E2.2.3). No hay
/// borrado físico: el query filter global lo oculta de futuras consultas.
/// </summary>
public sealed class EliminarHotelCommandHandler(IHotelRepository repositorio)
    : IRequestHandler<EliminarHotelCommand, Result>
{
    public async Task<Result> Handle(EliminarHotelCommand request, CancellationToken ct)
    {
        var hotel = await repositorio.ObtenerAsync(request.Id, ct);
        if (hotel is null)
        {
            return Result.NoEncontrado($"No existe un hotel activo con id {request.Id}.");
        }

        hotel.Eliminar();
        await repositorio.GuardarConcurrenciaAsync(hotel, request.RowVersion, ct);

        return Result.Ok();
    }
}
