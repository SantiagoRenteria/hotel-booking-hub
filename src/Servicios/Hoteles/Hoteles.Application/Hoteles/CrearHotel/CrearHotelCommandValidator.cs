using FluentValidation;
using Hoteles.Domain.Hoteles;

namespace Hoteles.Application.Hoteles.CrearHotel;

/// <summary>
/// Reglas de validación del alta (FR-1): nombre y ciudad obligatorios; estado válido; longitudes dentro de los
/// topes (<see cref="LongitudesHotel"/>, la misma fuente que usa el mapeo EF). Sin este tope, un valor
/// sobredimensionado pasaría la validación y reventaría en el INSERT (truncamiento → 500). → 400 (AC-E2.1.2).
/// </summary>
public sealed class CrearHotelCommandValidator : AbstractValidator<CrearHotelCommand>
{
    public CrearHotelCommandValidator()
    {
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

        RuleFor(x => x.Estado).IsInEnum().WithMessage("El estado del hotel no es válido.");
    }
}
