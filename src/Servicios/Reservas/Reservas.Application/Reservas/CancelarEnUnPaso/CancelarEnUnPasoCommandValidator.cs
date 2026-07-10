using FluentValidation;
using Reservas.Domain.Reservas;

namespace Reservas.Application.Reservas.CancelarEnUnPaso;

/// <summary>
/// Validación de entrada del atajo (→ 400): reserva, motivo (categoría + detalle, acotados a las columnas) e
/// iniciador/decisión válidos; al RECHAZAR, el motivo del rechazo es obligatorio. Los guards de negocio viven
/// en el dominio/handler (→ 409/403).
/// </summary>
public sealed class CancelarEnUnPasoCommandValidator : AbstractValidator<CancelarEnUnPasoCommand>
{
    public CancelarEnUnPasoCommandValidator()
    {
        RuleFor(x => x.ReservaId).NotEmpty().WithMessage("La reserva es obligatoria.");

        RuleFor(x => x.CategoriaMotivo)
            .NotEmpty().WithMessage("La categoría del motivo es obligatoria.")
            .MaximumLength(80).WithMessage("La categoría del motivo no puede exceder 80 caracteres.");

        RuleFor(x => x.DetalleMotivo)
            .NotEmpty().WithMessage("El detalle del motivo es obligatorio.")
            .MaximumLength(1000).WithMessage("El detalle del motivo no puede exceder 1000 caracteres.");

        RuleFor(x => x.Iniciador).IsInEnum().WithMessage("El iniciador de la cancelación no es válido.");
        RuleFor(x => x.Decision).IsInEnum().WithMessage("La decisión de resolución no es válida.");

        RuleFor(x => x.MotivoRechazo)
            .NotEmpty().WithMessage("El motivo del rechazo es obligatorio.")
            .When(x => x.Decision == DecisionCancelacion.Rechazar);

        RuleFor(x => x.MotivoRechazo)
            .MaximumLength(1000).WithMessage("El motivo no puede exceder 1000 caracteres.")
            .When(x => !string.IsNullOrEmpty(x.MotivoRechazo));
    }
}
