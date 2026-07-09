using FluentValidation;

namespace Reservas.Application.Reservas.SolicitarCancelacion;

/// <summary>
/// Validación de entrada del comando (AC-E4.1.1): la reserva y el motivo (categoría + detalle) son obligatorios
/// y el iniciador debe ser un valor conocido. El <c>ValidationBehavior</c> corta con 400 antes del handler; los
/// guards de negocio (estado no elegible, duplicada, estancia iniciada) viven en el dominio y producen 409.
/// </summary>
public sealed class SolicitarCancelacionCommandValidator : AbstractValidator<SolicitarCancelacionCommand>
{
    public SolicitarCancelacionCommandValidator()
    {
        RuleFor(x => x.ReservaId)
            .NotEmpty().WithMessage("La reserva es obligatoria.");

        RuleFor(x => x.CategoriaMotivo)
            .NotEmpty().WithMessage("La categoría del motivo es obligatoria.");

        RuleFor(x => x.DetalleMotivo)
            .NotEmpty().WithMessage("El detalle del motivo es obligatorio.");

        RuleFor(x => x.Iniciador)
            .IsInEnum().WithMessage("El iniciador de la cancelación no es válido.");
    }
}
