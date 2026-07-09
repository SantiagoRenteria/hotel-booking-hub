using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;

namespace Hoteles.Application.Hoteles.EditarHotel;

/// <summary>
/// Comando para editar los datos <b>descriptivos</b> de un hotel (AC-E2.2.1). NO incluye <c>Estado</c>: la
/// transición del ciclo de vida (habilitar/deshabilitar) es exclusiva de la Story 2.3. Transporta el
/// <see cref="RowVersion"/> que leyó el cliente (base64 en JSON → <c>byte[]</c>) como token de concurrencia
/// optimista: si otro editó/eliminó en medio, el guardado falla con 409 (nunca 500). No altera las habitaciones.
/// </summary>
public sealed record EditarHotelCommand(
    Guid Id,
    byte[] RowVersion,
    string Nombre,
    string Ciudad,
    string Direccion,
    string Descripcion) : ICommand<Result<HotelResponseDto>>;
