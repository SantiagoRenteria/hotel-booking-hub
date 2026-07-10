using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Reservas.ListarReservasDelAgente;

namespace Reservas.Application.Reservas.ObtenerReservaDetalle;

/// <summary>
/// Query del detalle de UNA reserva del agente actual (AC-E3.3.1). La identidad del agente se resuelve
/// server-side; el detalle de una reserva ajena responde <b>404</b> (no se filtra la existencia de datos ajenos).
/// </summary>
public sealed record ObtenerReservaDetalleQuery(Guid ReservaId) : IRequest<Result<ReservaDetalleDto>>;

/// <summary>Detalle de la reserva: los campos del listado + huéspedes y contacto de emergencia (FR-10/11).</summary>
public sealed record ReservaDetalleDto(
    ReservaListadoDto Reserva,
    IReadOnlyList<HuespedDetalleDto> Huespedes,
    ContactoEmergenciaDetalleDto? ContactoEmergencia);

/// <summary>Huésped en el detalle (FR-10).</summary>
public sealed record HuespedDetalleDto(
    string Nombres,
    string Apellidos,
    DateOnly FechaNacimiento,
    string Genero,
    string TipoDocumento,
    string NumeroDocumento,
    string Email,
    string Telefono);

/// <summary>Contacto de emergencia en el detalle (FR-11).</summary>
public sealed record ContactoEmergenciaDetalleDto(string NombreCompleto, string Telefono);
