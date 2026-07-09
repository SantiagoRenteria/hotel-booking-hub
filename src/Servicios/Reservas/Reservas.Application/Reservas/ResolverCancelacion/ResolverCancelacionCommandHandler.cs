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

    public async Task<Result<ResolucionCancelacionResponseDto>> Handle(ResolverCancelacionCommand request, CancellationToken ct)
    {
        // Identidad server-side, fail-closed (patrón 3.3): sin identidad no se resuelve nada.
        var agente = contexto.AgenteActual;
        if (string.IsNullOrWhiteSpace(agente))
        {
            return Result<ResolucionCancelacionResponseDto>.Prohibido("Se requiere la identidad del agente.");
        }

        agente = agente.Trim().ToLowerInvariant(); // canónico, igual que Reserva.Crear (aislamiento robusto)

        var reserva = await repositorio.ObtenerConNochesAsync(request.ReservaId, ct);
        if (reserva is null)
        {
            return Result<ResolucionCancelacionResponseDto>.NoEncontrado("La reserva no existe.");
        }

        // Aislamiento (AC-E4.2.4): un agente solo resuelve las cancelaciones de las reservas que intermedió.
        if (!string.Equals(reserva.AgenteEmail, agente, StringComparison.Ordinal))
        {
            return Result<ResolucionCancelacionResponseDto>.Prohibido("La reserva pertenece a otro agente.");
        }

        var fechaResolucion = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);

        // Guard de estado por excepción (→ 409). La concurrencia optimista (rowVersion) la arbitra el
        // EjecutorTransaccional (DbUpdateConcurrencyException → 409) en el commit.
        reserva.Resolver(request.Decision, agente, fechaResolucion, request.MotivoRechazo);

        var solicitud = reserva.SolicitudCancelacion!;
        var traceId = Activity.Current?.TraceId.ToString();
        // Destinatario del correo de resolución (Story 5.2/5.3, party-mode opción a): el viajero.
        var huespedEmail = reserva.Huespedes.FirstOrDefault()?.Email;

        if (solicitud.Resultado == ResultadoResolucion.Aprobada)
        {
            var data = new ReservaCanceladaV1(
                AggregateId: reserva.Id,
                ResueltaPor: agente,
                FechaResolucion: fechaResolucion,
                PenalidadAplicadaPorcentaje: solicitud.PenalidadAplicadaPorcentaje!.Value,
                PenalidadFueOverride: solicitud.PenalidadFueOverride,
                HuespedEmail: huespedEmail);
            outbox.Encolar(ReservaCanceladaV1.Tipo, VersionEvento, reserva.Id, data, traceId);
        }
        else
        {
            var data = new SolicitudCancelacionRechazadaV1(
                AggregateId: reserva.Id,
                ResueltaPor: agente,
                FechaResolucion: fechaResolucion,
                MotivoRechazo: solicitud.MotivoResolucion!,
                HuespedEmail: huespedEmail);
            outbox.Encolar(SolicitudCancelacionRechazadaV1.Tipo, VersionEvento, reserva.Id, data, traceId);
        }

        var dto = new ResolucionCancelacionResponseDto(
            reserva.Id,
            reserva.Estado.ToString(),
            solicitud.Resultado.ToString()!,
            solicitud.PenalidadAplicadaPorcentaje);

        return Result<ResolucionCancelacionResponseDto>.Ok(dto);
    }
}
