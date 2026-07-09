using Reservas.Application.Reservas.ResolverCancelacion;
using Reservas.Domain.Reservas;
using Xunit;

namespace Reservas.UnitTests.ResolverCancelacion;

/// <summary>Story 4.2 — validación de entrada (→ 400): reserva/decisión obligatorias; motivo obligatorio al rechazar.</summary>
public sealed class ResolverCancelacionCommandValidatorTests
{
    private readonly ResolverCancelacionCommandValidator _validator = new();

    [Fact]
    public void Aprobar_sin_motivo_es_valido()
    {
        var cmd = new ResolverCancelacionCommand(Guid.NewGuid(), DecisionCancelacion.AprobarAplicandoPenalidad, null);
        Assert.True(_validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void Rechazar_sin_motivo_es_invalido()
    {
        var cmd = new ResolverCancelacionCommand(Guid.NewGuid(), DecisionCancelacion.Rechazar, null);
        Assert.False(_validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void Rechazar_con_motivo_es_valido()
    {
        var cmd = new ResolverCancelacionCommand(Guid.NewGuid(), DecisionCancelacion.Rechazar, "No procede.");
        Assert.True(_validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void Reserva_vacia_es_invalida()
    {
        var cmd = new ResolverCancelacionCommand(Guid.Empty, DecisionCancelacion.AprobarAplicandoPenalidad, null);
        Assert.False(_validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void Decision_desconocida_es_invalida()
    {
        var cmd = new ResolverCancelacionCommand(Guid.NewGuid(), (DecisionCancelacion)99, null);
        Assert.False(_validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void Motivo_demasiado_largo_es_invalido()
    {
        var cmd = new ResolverCancelacionCommand(Guid.NewGuid(), DecisionCancelacion.Rechazar, new string('x', 1001));
        Assert.False(_validator.Validate(cmd).IsValid);
    }
}
