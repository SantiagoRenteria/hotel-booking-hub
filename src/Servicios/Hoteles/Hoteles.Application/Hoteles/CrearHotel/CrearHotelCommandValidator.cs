using FluentValidation;

namespace Hoteles.Application.Hoteles.CrearHotel;

/// <summary>Reglas de validación del alta (FR-1): nombre y ciudad obligatorios; estado válido. → 400 (AC-E2.1.2).</summary>
public sealed class CrearHotelCommandValidator : AbstractValidator<CrearHotelCommand>
{
    public CrearHotelCommandValidator()
    {
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("El nombre del hotel es obligatorio.");
        RuleFor(x => x.Ciudad).NotEmpty().WithMessage("La ciudad es obligatoria.");
        RuleFor(x => x.Estado).IsInEnum().WithMessage("El estado del hotel no es válido.");
    }
}
