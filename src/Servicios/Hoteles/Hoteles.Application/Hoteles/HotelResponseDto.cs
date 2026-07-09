namespace Hoteles.Application.Hoteles;

/// <summary>
/// Representación del recurso Hotel devuelta por los slices del catálogo (alta 201, edición 200): identidad
/// UUID v7, nombre, ciudad, estado y el <see cref="RowVersion"/> vigente (base64). Sin <c>Seq</c> (detalle de
/// persistencia). El <see cref="RowVersion"/> post-escritura deja al cliente listo para la siguiente edición
/// sin re-leer (aún no hay GET; llega en épica 3). Contrato único compartido por los slices del mismo recurso.
/// </summary>
public sealed record HotelResponseDto(Guid Id, string Nombre, string Ciudad, string Estado, string RowVersion);
