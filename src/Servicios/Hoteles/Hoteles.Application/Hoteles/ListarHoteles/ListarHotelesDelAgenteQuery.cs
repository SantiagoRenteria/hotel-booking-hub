using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Abstracciones;

namespace Hoteles.Application.Hoteles.ListarHoteles;

/// <summary>Query de lectura de los hoteles del agente actual (Story T.5, AC-ET.5.1). Aislamiento server-side.</summary>
public sealed record ListarHotelesDelAgenteQuery : IRequest<Result<IReadOnlyList<HotelVistaDto>>>;

public sealed class ListarHotelesDelAgenteQueryHandler(ILectorCatalogo lector, IContextoAgente contexto)
    : IRequestHandler<ListarHotelesDelAgenteQuery, Result<IReadOnlyList<HotelVistaDto>>>
{
    public async Task<Result<IReadOnlyList<HotelVistaDto>>> Handle(ListarHotelesDelAgenteQuery request, CancellationToken ct)
    {
        // Fail-closed (defensa en profundidad, igual que los handlers de escritura): sin identidad el query filter
        // se desactivaría (vería TODO) → se rechaza explícitamente en vez de devolver el catálogo de otros.
        if (string.IsNullOrWhiteSpace(contexto.AgenteActual))
        {
            return Result<IReadOnlyList<HotelVistaDto>>.Prohibido("Se requiere la identidad del agente.");
        }

        return Result<IReadOnlyList<HotelVistaDto>>.Ok(await lector.ListarHotelesAsync(ct));
    }
}
