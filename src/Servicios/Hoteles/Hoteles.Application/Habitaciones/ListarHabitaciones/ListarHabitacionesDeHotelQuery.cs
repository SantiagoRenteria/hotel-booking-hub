using FluentValidation;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Abstracciones;

namespace Hoteles.Application.Habitaciones.ListarHabitaciones;

/// <summary>
/// Habitaciones (paginadas) de un hotel del agente (Story T.5/T.6, AC-ET.6.2). 404 si el hotel no existe/es
/// ajeno/eliminado (no una página vacía que revele datos ajenos); página si el hotel es del agente.
/// </summary>
public sealed record ListarHabitacionesDeHotelQuery(Guid HotelId, int Page, int PageSize)
    : IRequest<Result<PaginaDto<HabitacionResponseDto>>>;

public sealed class ListarHabitacionesDeHotelQueryValidator : AbstractValidator<ListarHabitacionesDeHotelQuery>
{
    public ListarHabitacionesDeHotelQueryValidator()
    {
        // Cota superior de page: sin ella (page-1)*pageSize desborda Int32 → OFFSET negativo → 500.
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000).WithMessage("page debe estar entre 1 y 1.000.000.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("pageSize debe estar entre 1 y 100.");
    }
}

public sealed class ListarHabitacionesDeHotelQueryHandler(ILectorCatalogo lector, IContextoAgente contexto)
    : IRequestHandler<ListarHabitacionesDeHotelQuery, Result<PaginaDto<HabitacionResponseDto>>>
{
    public async Task<Result<PaginaDto<HabitacionResponseDto>>> Handle(ListarHabitacionesDeHotelQuery request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contexto.AgenteActual))
        {
            return Result<PaginaDto<HabitacionResponseDto>>.Prohibido("Se requiere la identidad del agente.");
        }

        var pagina = await lector.ListarHabitacionesDeHotelAsync(request.HotelId, request.Page, request.PageSize, ct);
        return pagina is null
            ? Result<PaginaDto<HabitacionResponseDto>>.NoEncontrado($"No existe un hotel {request.HotelId} para este agente.")
            : Result<PaginaDto<HabitacionResponseDto>>.Ok(pagina);
    }
}
