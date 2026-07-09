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

    public Task<Result<ResolucionCancelacionResponseDto>> Handle(CancelarEnUnPasoCommand request, CancellationToken ct)
    {
        _ = (repositorio, contexto, calculadorPenalidad, outbox, reloj, request, ct, VersionEvento);
        throw new NotImplementedException();
    }
}
