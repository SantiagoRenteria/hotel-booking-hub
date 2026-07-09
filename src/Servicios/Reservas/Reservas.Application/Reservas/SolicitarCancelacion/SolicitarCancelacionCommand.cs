using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Reservas.Domain.Reservas;

namespace Reservas.Application.Reservas.SolicitarCancelacion;

/// <summary>
/// Comando para solicitar la cancelación de una reserva confirmada (Story 4.1, FR-cancelación). Transporta el
/// motivo (categoría + texto libre) y quién la inicia; la fecha de solicitud NO viene del cliente — la resuelve
/// el handler vía <c>TimeProvider</c> para congelar la penalidad de forma determinista. La validación de formato
/// (motivo obligatorio) la aplica el <c>ValidationBehavior</c> (→ 400); los guards de estado son del dominio (→ 409).
/// </summary>
public sealed record SolicitarCancelacionCommand(
    Guid ReservaId,
    string CategoriaMotivo,
    string DetalleMotivo,
    IniciadorCancelacion Iniciador) : ICommand<Result<SolicitudCancelacionResponseDto>>;

/// <summary>
/// Respuesta de la solicitud: estado resultante + penalidad SUGERIDA congelada (porcentaje y monto informativo
/// sobre el precio ya congelado de la reserva). El monto es informativo — no se cobra (AC-E4.1.1).
/// </summary>
public sealed record SolicitudCancelacionResponseDto(
    Guid ReservaId,
    string Estado,
    decimal PenalidadPorcentaje,
    decimal PenalidadMonto);
