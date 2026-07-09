using FluentValidation;
using Hoteles.Domain.Hoteles;

namespace Hoteles.Application.Hoteles.EditarHotel;

/// <summary>
/// Validación de la edición (→ 400): id y rowVersion presentes, nombre/ciudad obligatorios y longitudes dentro
/// de los topes (<see cref="LongitudesHotel"/>, la misma fuente que usa el mapeo EF). No valida estado: la
/// edición no cambia el ciclo de vida (Story 2.3).
/// </summary>
public sealed class EditarHotelCommandValidator : AbstractValidator<EditarHotelCommand>
{
    public EditarHotelCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("El id del hotel es obligatorio.");

        RuleFor(x => x.RowVersion)
            .NotEmpty().WithMessage("El rowVersion es obligatorio para editar (control de concurrencia optimista).");

        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre del hotel es obligatorio.")
            .MaximumLength(LongitudesHotel.Nombre).WithMessage($"El nombre no puede exceder {LongitudesHotel.Nombre} caracteres.");

        RuleFor(x => x.Ciudad)
            .NotEmpty().WithMessage("La ciudad es obligatoria.")
            .MaximumLength(LongitudesHotel.Ciudad).WithMessage($"La ciudad no puede exceder {LongitudesHotel.Ciudad} caracteres.");

        RuleFor(x => x.Direccion)
            .MaximumLength(LongitudesHotel.Direccion).WithMessage($"La dirección no puede exceder {LongitudesHotel.Direccion} caracteres.");

        RuleFor(x => x.Descripcion)
            .MaximumLength(LongitudesHotel.Descripcion).WithMessage($"La descripción no puede exceder {LongitudesHotel.Descripcion} caracteres.");
    }
}
