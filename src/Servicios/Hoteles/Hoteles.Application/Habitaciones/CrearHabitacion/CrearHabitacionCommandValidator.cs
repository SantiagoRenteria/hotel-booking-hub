using FluentValidation;
using Hoteles.Domain.Habitaciones;

namespace Hoteles.Application.Habitaciones.CrearHabitacion;

/// <summary>Validación del alta de habitación (→ 400): hotel, tipo y ubicación presentes; montos ≥ 0; estado válido.</summary>
public sealed class CrearHabitacionCommandValidator : AbstractValidator<CrearHabitacionCommand>
{
    public CrearHabitacionCommandValidator()
    {
        RuleFor(x => x.HotelId).NotEmpty().WithMessage("El hotel de la habitación es obligatorio.");

        RuleFor(x => x.Tipo)
            .NotEmpty().WithMessage("El tipo de habitación es obligatorio.")
            .MaximumLength(LongitudesHabitacion.Tipo).WithMessage($"El tipo no puede exceder {LongitudesHabitacion.Tipo} caracteres.");

        RuleFor(x => x.Ubicacion)
            .NotEmpty().WithMessage("La ubicación es obligatoria.")
            .MaximumLength(LongitudesHabitacion.Ubicacion).WithMessage($"La ubicación no puede exceder {LongitudesHabitacion.Ubicacion} caracteres.");

        RuleFor(x => x.CostoBase)
            .GreaterThanOrEqualTo(0).WithMessage("El costo base no puede ser negativo.")
            .PrecisionScale(LongitudesHabitacion.PrecisionMonto, LongitudesHabitacion.EscalaMonto, ignoreTrailingZeros: false)
            .WithMessage($"El costo base excede la precisión permitida ({LongitudesHabitacion.PrecisionMonto},{LongitudesHabitacion.EscalaMonto}).");

        RuleFor(x => x.Impuestos)
            .GreaterThanOrEqualTo(0).WithMessage("Los impuestos no pueden ser negativos.")
            .PrecisionScale(LongitudesHabitacion.PrecisionMonto, LongitudesHabitacion.EscalaMonto, ignoreTrailingZeros: false)
            .WithMessage($"Los impuestos exceden la precisión permitida ({LongitudesHabitacion.PrecisionMonto},{LongitudesHabitacion.EscalaMonto}).");
        RuleFor(x => x.Estado).IsInEnum().WithMessage("El estado de la habitación no es válido.");
    }
}
