using Hoteles.Domain.Hoteles;

namespace Hoteles.Application.Hoteles;

/// <summary>
/// Vista de lectura de un hotel (Story T.5): a diferencia de <see cref="HotelResponseDto"/> (respuesta de
/// escritura, mínima), incluye <see cref="Direccion"/> y <see cref="Descripcion"/> para poder mostrar/editar el
/// recurso completo tras un GET. Trae el <see cref="RowVersion"/> vigente (base64) → el cliente puede editar
/// (GET → PUT) sin depender de la respuesta de una escritura previa.
/// </summary>
public sealed record HotelVistaDto(
    Guid Id,
    string Nombre,
    string Ciudad,
    string Direccion,
    string Descripcion,
    string Estado,
    string RowVersion)
{
    public static HotelVistaDto De(Hotel h, byte[] rowVersion) => new(
        h.Id, h.Nombre, h.Ciudad, h.Direccion, h.Descripcion, h.Estado.ToString(), Convert.ToBase64String(rowVersion));
}
