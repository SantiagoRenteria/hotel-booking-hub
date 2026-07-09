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

    public Task<Result<SolicitudCancelacionResponseDto>> Handle(SolicitarCancelacionCommand request, CancellationToken ct)
    {
        _ = (repositorio, calculadorPenalidad, outbox, reloj, request, ct, VersionEvento);
        throw new NotImplementedException();
    }
}
