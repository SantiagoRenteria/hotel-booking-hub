using System.Diagnostics;
using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.ResolverCancelacion;
using Reservas.Domain.Puertos;
using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;

namespace Reservas.Application.Reservas.CancelarEnUnPaso;

/// <summary>
/// Compone solicitar (4.1) + resolver (4.2) en UNA transacción y emite AMBOS eventos por el outbox multi-evento
/// (Task 0): <c>SolicitudCancelacionRegistrada.v1</c> ANTES que la resolución, ambos con <c>AggregateId=ReservaId</c>.
/// Identidad server-side (403 sin identidad); aislamiento por <c>AgenteEmail</c> (ajeno → 403); guards de dominio
/// → 409. NO recalcula la penalidad. No duplica lógica de transición: llama a los métodos del agregado existentes.
/// </summary>
public sealed class CancelarEnUnPasoCommandHandler(
    IReservaRepository repositorio,
    IContextoAgente contexto,
    CalculadorPenalidad calculadorPenalidad,
    IColaOutbox outbox,
    TimeProvider reloj)
    : IRequestHandler<CancelarEnUnPasoCommand, Result<ResolucionCancelacionResponseDto>>
{
    private const int VersionEvento = 1;

    public async Task<Result<ResolucionCancelacionResponseDto>> Handle(CancelarEnUnPasoCommand request, CancellationToken ct)
    {
        var agente = contexto.AgenteActual;
        if (string.IsNullOrWhiteSpace(agente))
        {
            return Result<ResolucionCancelacionResponseDto>.Prohibido("Se requiere la identidad del agente.");
        }

        agente = agente.Trim().ToLowerInvariant();

        var reserva = await repositorio.ObtenerConNochesAsync(request.ReservaId, ct);
        if (reserva is null)
        {
            return Result<ResolucionCancelacionResponseDto>.NoEncontrado("La reserva no existe.");
        }

        if (!string.Equals(reserva.AgenteEmail, agente, StringComparison.Ordinal))
        {
            return Result<ResolucionCancelacionResponseDto>.Prohibido("La reserva pertenece a otro agente.");
        }

        var fecha = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
        var motivo = MotivoCancelacion.Crear(request.CategoriaMotivo, request.DetalleMotivo);
        var traceId = Activity.Current?.TraceId.ToString();

        // Composición: solicitar (4.1) + resolver (4.2) sobre el MISMO agregado trackeado (guards → 409).
        var penalidadSugerida = reserva.SolicitarCancelacion(motivo, request.Iniciador, fecha, calculadorPenalidad);
        reserva.Resolver(request.Decision, agente, fecha, request.MotivoRechazo);

        // AMBOS eventos por auditoría (AC-E4.3.1): solicitud ANTES que resolución (order key = ReservaId).
        var solicitud = reserva.SolicitudCancelacion!;
        outbox.Encolar(
            SolicitudCancelacionRegistradaV1.Tipo, VersionEvento, reserva.Id,
            new SolicitudCancelacionRegistradaV1(
                AggregateId: reserva.Id,
                Iniciador: request.Iniciador.ToString(),
                MotivoCategoria: motivo.Categoria,
                MotivoDetalle: motivo.Detalle,
                PenalidadPorcentaje: penalidadSugerida.Porcentaje,
                FechaSolicitud: fecha,
                // Destinatarios de la notificación (Story 5.2), tomados de la reserva (party-mode opción a).
                HuespedEmail: reserva.Huespedes.FirstOrDefault()?.Email,
                AgenteEmail: reserva.AgenteEmail),
            traceId);

        if (solicitud.Resultado == ResultadoResolucion.Aprobada)
        {
            outbox.Encolar(
                ReservaCanceladaV1.Tipo, VersionEvento, reserva.Id,
                new ReservaCanceladaV1(
                    AggregateId: reserva.Id,
                    ResueltaPor: agente,
                    FechaResolucion: fecha,
                    PenalidadAplicadaPorcentaje: solicitud.PenalidadAplicadaPorcentaje!.Value,
                    PenalidadFueOverride: solicitud.PenalidadFueOverride,
                    HuespedEmail: reserva.Huespedes.FirstOrDefault()?.Email),
                traceId);
        }
        else
        {
            outbox.Encolar(
                SolicitudCancelacionRechazadaV1.Tipo, VersionEvento, reserva.Id,
                new SolicitudCancelacionRechazadaV1(
                    AggregateId: reserva.Id,
                    ResueltaPor: agente,
                    FechaResolucion: fecha,
                    MotivoRechazo: solicitud.MotivoResolucion!,
                    HuespedEmail: reserva.Huespedes.FirstOrDefault()?.Email),
                traceId);
        }

        var dto = new ResolucionCancelacionResponseDto(
            reserva.Id,
            reserva.Estado.ToString(),
            solicitud.Resultado.ToString()!,
            solicitud.PenalidadAplicadaPorcentaje);

        return Result<ResolucionCancelacionResponseDto>.Ok(dto);
    }
}
