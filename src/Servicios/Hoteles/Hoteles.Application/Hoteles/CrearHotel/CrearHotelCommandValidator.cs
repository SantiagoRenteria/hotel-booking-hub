using FluentValidation;

namespace Hoteles.Application.Hoteles.CrearHotel;

/// <summary>Reglas de validación del alta (FR-1): nombre y ciudad obligatorios; estado válido. → 400 (AC-E2.1.2).</summary>
public sealed class CrearHotelCommandValidator : AbstractValidator<CrearHotelCommand>
{
    // Deben coincidir con los HasMaxLength de HotelesDbContext: sin este tope, un valor sobredimensionado
    // pasaría la validación y reventaría en el INSERT (truncamiento → 500) en vez de devolver 400.
    private const int MaxNombre = 200;
    private const int MaxCiudad = 120;
    private const int MaxDireccion = 300;
    private const int MaxDescripcion = 2000;

    public CrearHotelCommandValidator()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre del hotel es obligatorio.")
            .MaximumLength(MaxNombre).WithMessage($"El nombre no puede exceder {MaxNombre} caracteres.");

        RuleFor(x => x.Ciudad)
            .NotEmpty().WithMessage("La ciudad es obligatoria.")
            .MaximumLength(MaxCiudad).WithMessage($"La ciudad no puede exceder {MaxCiudad} caracteres.");

        RuleFor(x => x.Direccion)
            .MaximumLength(MaxDireccion).WithMessage($"La dirección no puede exceder {MaxDireccion} caracteres.");

        RuleFor(x => x.Descripcion)
            .MaximumLength(MaxDescripcion).WithMessage($"La descripción no puede exceder {MaxDescripcion} caracteres.");

        RuleFor(x => x.Estado).IsInEnum().WithMessage("El estado del hotel no es válido.");
    }
}
