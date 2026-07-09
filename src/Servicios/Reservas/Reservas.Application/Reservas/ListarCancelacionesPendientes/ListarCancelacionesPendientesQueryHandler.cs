using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Abstracciones;

namespace Reservas.Application.Reservas.ListarCancelacionesPendientes;

/// <summary>
/// Resuelve la identidad del agente server-side (fail-closed → 403), lee sus cancelaciones pendientes aisladas y
/// deriva los "días en espera" = <c>hoy - fechaSolicitud</c> con el <see cref="TimeProvider"/> inyectable
/// (determinista para tests). Sin expiración automática (AC-E4.3.3): solo expone la antigüedad.
/// </summary>
public sealed class ListarCancelacionesPendientesQueryHandler(
    ILectorCancelacionesPendientes lector,
    IContextoAgente contexto,
    TimeProvider reloj)
    : IRequestHandler<ListarCancelacionesPendientesQuery, Result<IReadOnlyList<CancelacionPendienteDto>>>
{
    public async Task<Result<IReadOnlyList<CancelacionPendienteDto>>> Handle(ListarCancelacionesPendientesQuery request, CancellationToken ct)
    {
        var agente = contexto.AgenteActual;
        if (string.IsNullOrWhiteSpace(agente))
        {
            return Result<IReadOnlyList<CancelacionPendienteDto>>.Prohibido("Se requiere la identidad del agente.");
        }

        var filas = await lector.ListarPendientesAsync(agente.Trim().ToLowerInvariant(), ct);
        var hoy = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);

        var items = filas
            .Select(f => new CancelacionPendienteDto(
                f.ReservaId,
                f.FechaSolicitud,
                hoy.DayNumber - f.FechaSolicitud.DayNumber, // días en espera; sin expiración automática
                f.MotivoCategoria,
                f.PenalidadPorcentaje))
            .ToList();

        return Result<IReadOnlyList<CancelacionPendienteDto>>.Ok(items);
    }
}
