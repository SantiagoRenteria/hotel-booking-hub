using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;

namespace Hoteles.Application.Hoteles.EliminarHotel;

/// <summary>
/// Comando para dar de baja lógica un hotel (AC-E2.2.2). Devuelve <see cref="Result"/> sin payload (→ 204).
/// Transporta el <see cref="RowVersion"/> del cliente para arbitrar la baja bajo concurrencia optimista (409
/// si otro modificó/eliminó en medio).
/// </summary>
public sealed record EliminarHotelCommand(Guid Id, byte[] RowVersion) : ICommand<Result>;
