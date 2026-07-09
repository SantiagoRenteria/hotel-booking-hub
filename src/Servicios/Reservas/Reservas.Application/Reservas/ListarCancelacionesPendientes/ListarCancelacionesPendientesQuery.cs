using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;

namespace Reservas.Application.Reservas.ListarCancelacionesPendientes;

/// <summary>
/// Visibilidad operativa (Story 4.3, AC-E4.3.3): lista las solicitudes de cancelación PENDIENTES del agente,
/// cada una con sus "días en espera". Aislada server-side por identidad del agente (patrón 3.3). NO hay
/// expiración automática: solo se EXPONE la antigüedad.
/// </summary>
public sealed record ListarCancelacionesPendientesQuery
    : IRequest<Result<IReadOnlyList<CancelacionPendienteDto>>>;

/// <summary>Una solicitud pendiente con su antigüedad. La penalidad es la SUGERIDA congelada (informativa).</summary>
public sealed record CancelacionPendienteDto(
    Guid ReservaId,
    DateOnly FechaSolicitud,
    int DiasEnEspera,
    string MotivoCategoria,
    decimal PenalidadSugeridaPorcentaje);
