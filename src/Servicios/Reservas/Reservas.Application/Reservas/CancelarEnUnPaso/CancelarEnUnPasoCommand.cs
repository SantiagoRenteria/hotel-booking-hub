using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Reservas.ResolverCancelacion;
using Reservas.Domain.Reservas;

namespace Reservas.Application.Reservas.CancelarEnUnPaso;

/// <summary>
/// Atajo de un paso (Story 4.3, AC-E4.3.1): solicita y resuelve la cancelación en UN comando/transacción, para
/// que el agente atienda al viajero por teléfono sin perder trazabilidad. Compone las transiciones de dominio de
/// 4.1 (<c>SolicitarCancelacion</c>) y 4.2 (<c>Resolver</c>) sobre el MISMO agregado y emite AMBOS eventos por el
/// outbox multi-evento (Task 0). Identidad del agente server-side (aislamiento → 403); fecha vía <c>TimeProvider</c>.
/// </summary>
public sealed record CancelarEnUnPasoCommand(
    Guid ReservaId,
    string CategoriaMotivo,
    string DetalleMotivo,
    IniciadorCancelacion Iniciador,
    DecisionCancelacion Decision,
    string? MotivoRechazo) : ICommand<Result<ResolucionCancelacionResponseDto>>;

/// <summary>Cuerpo HTTP del atajo; el <c>ReservaId</c> viaja en la ruta y la identidad del agente server-side.</summary>
public sealed record CancelarEnUnPasoRequest(
    string CategoriaMotivo,
    string DetalleMotivo,
    IniciadorCancelacion Iniciador,
    DecisionCancelacion Decision,
    string? MotivoRechazo);
