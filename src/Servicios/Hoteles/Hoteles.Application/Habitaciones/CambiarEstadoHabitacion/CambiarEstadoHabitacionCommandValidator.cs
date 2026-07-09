using FluentValidation;

namespace Hoteles.Application.Habitaciones.CambiarEstadoHabitacion;

/// <summary>Validación de la transición (→ 400): id y rowVersion presentes; estado objetivo válido.</summary>
public sealed class CambiarEstadoHabitacionCommandValidator : AbstractValidator<CambiarEstadoHabitacionCommand>
{
    public CambiarEstadoHabitacionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("El id de la habitación es obligatorio.");

        RuleFor(x => x.RowVersion)
            .NotEmpty().WithMessage("El rowVersion es obligatorio (control de concurrencia optimista).");

        RuleFor(x => x.EstadoObjetivo).IsInEnum().WithMessage("El estado objetivo no es válido.");
    }
}
