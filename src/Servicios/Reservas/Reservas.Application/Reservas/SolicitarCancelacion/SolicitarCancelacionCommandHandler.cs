using System.Diagnostics;
using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Abstracciones;
using Reservas.Domain.Puertos;
using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;

namespace Reservas.Application.Reservas.SolicitarCancelacion;

/// <summary>
/// Orquesta la solicitud de cancelación (AC-E4.1.1/.2/.3): carga la reserva (tracking), resuelve la fecha de
/// solicitud desde el <see cref="TimeProvider"/> (para congelar la penalidad de forma determinista), delega en el
/// agregado (guards por excepción → 409) y ENCOLA <c>SolicitudCancelacionRegistrada.v1</c> en el outbox. Pasa por
/// el <c>TransactionBehavior</c>: cambio de dominio + outbox en UNA transacción (patrón de 1.6b). No captura las
/// excepciones de dominio: el <c>ManejadorExcepcionesNegocio</c> las traduce a 409.
/// </summary>
public sealed class SolicitarCancelacionCommandHandler(
    IReservaRepository repositorio,
    CalculadorPenalidad calculadorPenalidad,
    IColaOutbox outbox,
    TimeProvider reloj)
    : IRequestHandler<SolicitarCancelacionCommand, Result<SolicitudCancelacionResponseDto>>
{
    private const int VersionEvento = 1;

    public async Task<Result<SolicitudCancelacionResponseDto>> Handle(SolicitarCancelacionCommand request, CancellationToken ct)
    {
        var reserva = await repositorio.ObtenerAsync(request.ReservaId, ct);
        if (reserva is null)
        {
            return Result<SolicitudCancelacionResponseDto>.NoEncontrado("La reserva no existe.");
        }

        // El reloj se resuelve AQUÍ (no en el dominio) y se pasa como DateOnly: el cálculo de penalidad queda
        // determinista y la penalidad se congela con esta fecha (patrón Task 0).
        var fechaSolicitud = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
        var motivo = MotivoCancelacion.Crear(request.CategoriaMotivo, request.DetalleMotivo);

        // Guards de dominio por excepción (TransicionEstadoInvalidaException → 409 vía handler transversal): no
        // se capturan. Si pasa, el estado transiciona y la penalidad queda congelada dentro del agregado.
        var penalidad = reserva.SolicitarCancelacion(motivo, request.Iniciador, fechaSolicitud, calculadorPenalidad);

        var data = new SolicitudCancelacionRegistradaV1(
            AggregateId: reserva.Id,
            Iniciador: request.Iniciador.ToString(),
            MotivoCategoria: motivo.Categoria,
            MotivoDetalle: motivo.Detalle,
            PenalidadPorcentaje: penalidad.Porcentaje,
            FechaSolicitud: fechaSolicitud);

        outbox.Encolar(SolicitudCancelacionRegistradaV1.Tipo, VersionEvento, reserva.Id, data, Activity.Current?.TraceId.ToString());

        var dto = new SolicitudCancelacionResponseDto(
            reserva.Id,
            reserva.Estado.ToString(),
            penalidad.Porcentaje,
            penalidad.MontoSobre(reserva.PrecioTotal));

        return Result<SolicitudCancelacionResponseDto>.Ok(dto);
    }
}
