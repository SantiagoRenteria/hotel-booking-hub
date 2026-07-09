using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Reservas.Domain.Reservas;

namespace Reservas.Application.Reservas.ResolverCancelacion;

/// <summary>
/// Comando para que un agente RESUELVA una solicitud de cancelación (Story 4.2): aprobar aplicando la penalidad
/// congelada, aprobar condonándola, o rechazar (con motivo). La identidad del agente NO viaja en el comando — se
/// resuelve server-side (<c>IContextoAgente</c>, patrón 3.3) para el aislamiento (AC-E4.2.4). La fecha de
/// resolución la pone el handler (<c>TimeProvider</c>). Guards de estado en el dominio (→ 409); ajeno → 403.
/// </summary>
public sealed record ResolverCancelacionCommand(
    Guid ReservaId,
    DecisionCancelacion Decision,
    string? MotivoRechazo) : ICommand<Result<ResolucionCancelacionResponseDto>>;

/// <summary>Respuesta de la resolución: estado resultante, desenlace y penalidad efectivamente aplicada (si aprobó).</summary>
public sealed record ResolucionCancelacionResponseDto(
    Guid ReservaId,
    string Estado,
    string Resultado,
    decimal? PenalidadAplicadaPorcentaje);

/// <summary>Cuerpo HTTP de la resolución; el <c>ReservaId</c> viaja en la ruta y la identidad del agente server-side.</summary>
public sealed record ResolverCancelacionRequest(
    DecisionCancelacion Decision,
    string? MotivoRechazo);
