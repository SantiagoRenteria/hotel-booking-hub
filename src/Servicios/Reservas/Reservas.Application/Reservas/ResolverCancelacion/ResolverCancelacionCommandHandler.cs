using System.Diagnostics;
using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Abstracciones;
using Reservas.Domain.Puertos;
using Reservas.Domain.Reservas;

namespace Reservas.Application.Reservas.ResolverCancelacion;

/// <summary>
/// Orquesta la resolución (AC-E4.2.1/.2/.3/.4): resuelve la identidad del agente server-side (fail-closed → 403),
/// carga la reserva CON sus slots (para liberarlos en la misma tx), aplica el aislamiento por agente (ajeno → 403),
/// delega en el agregado (guards por excepción → 409; la concurrencia optimista la arbitra el
/// <c>EjecutorTransaccional</c> traduciendo <c>DbUpdateConcurrencyException</c> → 409) y ENCOLA el evento según el
/// desenlace (<c>ReservaCancelada.v1</c> al aprobar, <c>SolicitudCancelacionRechazada.v1</c> al rechazar) en la
/// misma transacción del outbox. NO recalcula la penalidad.
/// </summary>
public sealed class ResolverCancelacionCommandHandler(
    IReservaRepository repositorio,
    IContextoAgente contexto,
    IColaOutbox outbox,
    TimeProvider reloj)
    : IRequestHandler<ResolverCancelacionCommand, Result<ResolucionCancelacionResponseDto>>
{
    private const int VersionEvento = 1;

    public Task<Result<ResolucionCancelacionResponseDto>> Handle(ResolverCancelacionCommand request, CancellationToken ct)
    {
        _ = (repositorio, contexto, outbox, reloj, request, ct, VersionEvento);
        throw new NotImplementedException();
    }
}
