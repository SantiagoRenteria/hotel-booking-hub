using FluentValidation;

namespace Reservas.Application.Reservas.BuscarDisponibilidad;

/// <summary>
/// Validación de entrada de la búsqueda (AC-E3.2.1): ciudad obligatoria, rango semiabierto coherente
/// (<c>Salida &gt; Entrada</c>) y al menos un huésped. El <c>ValidationBehavior</c> corta con 400 antes del handler.
/// </summary>
public sealed class BuscarDisponibilidadQueryValidator : AbstractValidator<BuscarDisponibilidadQuery>
{
    // Mismo tope que la creación de reservas: acota rangos absurdos que barrerían un año de slots.
    private const int MaxNoches = 365;

    public BuscarDisponibilidadQueryValidator()
    {
        RuleFor(x => x.Ciudad)
            .NotEmpty().WithMessage("La ciudad es obligatoria.");

        RuleFor(x => x.Huespedes)
            .GreaterThanOrEqualTo(1).WithMessage("Debe indicar al menos un huésped.");

        RuleFor(x => x.Salida)
            .GreaterThan(x => x.Entrada).WithMessage("La fecha de salida debe ser posterior a la de entrada.")
            .Must((consulta, salida) => salida.DayNumber - consulta.Entrada.DayNumber <= MaxNoches)
                .WithMessage($"El rango de búsqueda no puede exceder {MaxNoches} noches.")
                .When(consulta => consulta.Salida > consulta.Entrada, ApplyConditionTo.CurrentValidator);
    }
}
