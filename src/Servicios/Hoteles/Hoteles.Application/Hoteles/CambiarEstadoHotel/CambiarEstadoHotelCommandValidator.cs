using FluentValidation;

namespace Hoteles.Application.Hoteles.CambiarEstadoHotel;

/// <summary>
/// Validación de la transición (→ 400): id y rowVersion presentes; estado objetivo válido (defensivo — el
/// endpoint dedicado siempre lo fija correctamente).
/// </summary>
public sealed class CambiarEstadoHotelCommandValidator : AbstractValidator<CambiarEstadoHotelCommand>
{
    public CambiarEstadoHotelCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("El id del hotel es obligatorio.");

        RuleFor(x => x.RowVersion)
            .NotEmpty().WithMessage("El rowVersion es obligatorio (control de concurrencia optimista).");

        RuleFor(x => x.EstadoObjetivo).IsInEnum().WithMessage("El estado objetivo no es válido.");
    }
}
