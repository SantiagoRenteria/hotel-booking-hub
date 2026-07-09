using Hoteles.Domain.Habitaciones;

namespace Hoteles.Application.Habitaciones;

/// <summary>
/// Representación del recurso Habitación devuelta por sus slices (alta 201, edición/estado 200): identidad,
/// hotel al que pertenece, datos de catálogo, estado y el <c>rowVersion</c> vigente (base64) — igual que
/// <c>HotelResponseDto</c>, para permitir la siguiente operación sin re-leer.
/// </summary>
public sealed record HabitacionResponseDto(
    Guid Id,
    Guid HotelId,
    string Tipo,
    decimal CostoBase,
    decimal Impuestos,
    string Ubicacion,
    string Estado,
    string RowVersion)
{
    /// <summary>Proyecta la habitación y su rowversion post-escritura al DTO (fuente única del mapeo).</summary>
    public static HabitacionResponseDto De(Habitacion h, byte[] rowVersion) => new(
        h.Id, h.HotelId, h.Tipo, h.CostoBase, h.Impuestos, h.Ubicacion, h.Estado.ToString(), Convert.ToBase64String(rowVersion));
}
