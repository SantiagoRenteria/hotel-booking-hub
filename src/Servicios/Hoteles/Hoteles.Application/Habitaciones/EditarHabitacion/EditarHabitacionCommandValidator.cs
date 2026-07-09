using FluentValidation;
using Hoteles.Domain.Habitaciones;

namespace Hoteles.Application.Habitaciones.EditarHabitacion;

/// <summary>Validación de la edición (→ 400): id y rowVersion presentes; tipo/ubicación presentes; montos ≥ 0.</summary>
public sealed class EditarHabitacionCommandValidator : AbstractValidator<EditarHabitacionCommand>
{
    public EditarHabitacionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("El id de la habitación es obligatorio.");

        RuleFor(x => x.RowVersion)
            .NotEmpty().WithMessage("El rowVersion es obligatorio para editar (control de concurrencia optimista).");

        RuleFor(x => x.Tipo)
            .NotEmpty().WithMessage("El tipo de habitación es obligatorio.")
            .MaximumLength(LongitudesHabitacion.Tipo).WithMessage($"El tipo no puede exceder {LongitudesHabitacion.Tipo} caracteres.");

        RuleFor(x => x.Ubicacion)
            .NotEmpty().WithMessage("La ubicación es obligatoria.")
            .MaximumLength(LongitudesHabitacion.Ubicacion).WithMessage($"La ubicación no puede exceder {LongitudesHabitacion.Ubicacion} caracteres.");

        RuleFor(x => x.CostoBase).GreaterThanOrEqualTo(0).WithMessage("El costo base no puede ser negativo.");
        RuleFor(x => x.Impuestos).GreaterThanOrEqualTo(0).WithMessage("Los impuestos no pueden ser negativos.");
    }
}
