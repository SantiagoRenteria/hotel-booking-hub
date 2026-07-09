using FluentValidation;

namespace Hoteles.Application.Hoteles.EliminarHotel;

/// <summary>Validación de la baja (→ 400): id y rowVersion presentes (el rowVersion arbitra la concurrencia).</summary>
public sealed class EliminarHotelCommandValidator : AbstractValidator<EliminarHotelCommand>
{
    public EliminarHotelCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("El id del hotel es obligatorio.");

        RuleFor(x => x.RowVersion)
            .NotEmpty().WithMessage("El rowVersion es obligatorio para eliminar (control de concurrencia optimista).");
    }
}
