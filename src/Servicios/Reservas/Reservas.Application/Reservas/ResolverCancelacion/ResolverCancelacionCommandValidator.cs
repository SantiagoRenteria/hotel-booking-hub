using FluentValidation;
using Reservas.Domain.Reservas;

namespace Reservas.Application.Reservas.ResolverCancelacion;

/// <summary>
/// Validación de entrada (→ 400): reserva y decisión obligatorias; al RECHAZAR, el motivo es obligatorio
/// (AC-E4.2.2) y acotado a la columna. Los guards de negocio (estado no resoluble, doble resolución, agente
/// ajeno) viven en el dominio/handler (→ 409/403).
/// </summary>
public sealed class ResolverCancelacionCommandValidator : AbstractValidator<ResolverCancelacionCommand>
{
    public ResolverCancelacionCommandValidator()
    {
        RuleFor(x => x.ReservaId)
            .NotEmpty().WithMessage("La reserva es obligatoria.");

        RuleFor(x => x.Decision)
            .IsInEnum().WithMessage("La decisión de resolución no es válida.");

        // El motivo es obligatorio solo al rechazar; espeja la columna MotivoResolucion nvarchar(1000).
        RuleFor(x => x.MotivoRechazo)
            .NotEmpty().WithMessage("El motivo del rechazo es obligatorio.")
            .When(x => x.Decision == DecisionCancelacion.Rechazar);

        RuleFor(x => x.MotivoRechazo)
            .MaximumLength(1000).WithMessage("El motivo no puede exceder 1000 caracteres.")
            .When(x => !string.IsNullOrEmpty(x.MotivoRechazo));
    }
}
