using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Abstracciones;

namespace Reservas.Application.Reservas.ListarReservasDelAgente;

/// <summary>
/// Resuelve la identidad del agente server-side (<see cref="IContextoAgente"/>) y delega el listado aislado en el
/// <see cref="ILectorReservasAgente"/>. <b>Fail-closed:</b> sin identidad → 403 (no devuelve todo). Un agente sin
/// reservas → 200 con lista vacía.
/// </summary>
public sealed class ListarReservasDelAgenteQueryHandler(ILectorReservasAgente lector, IContextoAgente contexto)
    : IRequestHandler<ListarReservasDelAgenteQuery, Result<IReadOnlyList<ReservaListadoDto>>>
{
    public async Task<Result<IReadOnlyList<ReservaListadoDto>>> Handle(ListarReservasDelAgenteQuery request, CancellationToken ct)
    {
        var agente = contexto.AgenteActual;
        if (string.IsNullOrWhiteSpace(agente))
        {
            return Result<IReadOnlyList<ReservaListadoDto>>.Prohibido("Se requiere la identidad del agente.");
        }

        var reservas = await lector.ListarAsync(agente, ct);
        return Result<IReadOnlyList<ReservaListadoDto>>.Ok(reservas);
    }
}
