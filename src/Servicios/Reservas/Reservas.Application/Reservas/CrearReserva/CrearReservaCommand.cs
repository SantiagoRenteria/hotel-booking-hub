using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;

namespace Reservas.Application.Reservas.CrearReserva;

/// <summary>
/// Comando para crear-confirmar una reserva en una sola operación (FR-9/10/11). Transporta la petición
/// del viajero; la validación de formato/obligatoriedad la aplica el <c>ValidationBehavior</c> (→ 400).
/// </summary>
public sealed record CrearReservaCommand(
    Guid HabitacionId,
    DateOnly Entrada,
    DateOnly Salida,
    IReadOnlyList<HuespedDto> Huespedes,
    ContactoEmergenciaDto ContactoEmergencia,
    string AgenteEmail) : IRequest<Result<ReservaResponseDto>>;

/// <summary>Datos de un huésped en la petición (FR-10). Todos obligatorios.</summary>
public sealed record HuespedDto(
    string Nombres,
    string Apellidos,
    DateOnly FechaNacimiento,
    string Genero,
    string TipoDocumento,
    string NumeroDocumento,
    string Email,
    string Telefono);

/// <summary>Contacto de emergencia en la petición (FR-11).</summary>
public sealed record ContactoEmergenciaDto(string NombreCompleto, string Telefono);
