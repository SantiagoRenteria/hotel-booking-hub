using FluentValidation;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Abstracciones;

namespace Hoteles.Application.Hoteles.ListarHoteles;

/// <summary>Query paginada de los hoteles del agente actual (Story T.5/T.6, AC-ET.6.1). Aislamiento server-side.</summary>
public sealed record ListarHotelesDelAgenteQuery(int Page, int PageSize) : IRequest<Result<PaginaDto<HotelVistaDto>>>;

/// <summary>Valida la paginación (→ 400 vía ValidationBehavior si `page`/`pageSize` están fuera de rango).</summary>
public sealed class ListarHotelesDelAgenteQueryValidator : AbstractValidator<ListarHotelesDelAgenteQuery>
{
    public ListarHotelesDelAgenteQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("page debe ser ≥ 1.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("pageSize debe estar entre 1 y 100.");
    }
}

public sealed class ListarHotelesDelAgenteQueryHandler(ILectorCatalogo lector, IContextoAgente contexto)
    : IRequestHandler<ListarHotelesDelAgenteQuery, Result<PaginaDto<HotelVistaDto>>>
{
    public async Task<Result<PaginaDto<HotelVistaDto>>> Handle(ListarHotelesDelAgenteQuery request, CancellationToken ct)
    {
        // Fail-closed (defensa en profundidad, igual que los handlers de escritura): sin identidad el query filter
        // se desactivaría (vería TODO) → se rechaza explícitamente en vez de devolver el catálogo de otros.
        if (string.IsNullOrWhiteSpace(contexto.AgenteActual))
        {
            return Result<PaginaDto<HotelVistaDto>>.Prohibido("Se requiere la identidad del agente.");
        }

        return Result<PaginaDto<HotelVistaDto>>.Ok(await lector.ListarHotelesAsync(request.Page, request.PageSize, ct));
    }
}
