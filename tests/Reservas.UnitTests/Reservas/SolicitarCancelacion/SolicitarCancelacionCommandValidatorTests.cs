using Reservas.Application.Reservas.SolicitarCancelacion;
using Reservas.Domain.Reservas;
using Xunit;

namespace Reservas.UnitTests.SolicitarCancelacion;

/// <summary>Story 4.1 — validación de entrada (→ 400): motivo obligatorio y iniciador conocido.</summary>
public sealed class SolicitarCancelacionCommandValidatorTests
{
    private readonly SolicitarCancelacionCommandValidator _validator = new();

    private static SolicitarCancelacionCommand Valido() =>
        new(Guid.NewGuid(), "CambioDePlanes", "Motivo detallado.", IniciadorCancelacion.Viajero);

    [Fact]
    public void Comando_valido_pasa()
    {
        Assert.True(_validator.Validate(Valido()).IsValid);
    }

    [Theory]
    [InlineData("", "detalle")]
    [InlineData("categoria", "")]
    [InlineData(" ", "detalle")]
    public void Motivo_incompleto_es_invalido(string categoria, string detalle)
    {
        var comando = Valido() with { CategoriaMotivo = categoria, DetalleMotivo = detalle };
        Assert.False(_validator.Validate(comando).IsValid);
    }

    [Fact]
    public void Reserva_vacia_es_invalida()
    {
        var comando = Valido() with { ReservaId = Guid.Empty };
        Assert.False(_validator.Validate(comando).IsValid);
    }

    [Fact]
    public void Iniciador_desconocido_es_invalido()
    {
        var comando = Valido() with { Iniciador = (IniciadorCancelacion)99 };
        Assert.False(_validator.Validate(comando).IsValid);
    }

    // Review][Patch] — un motivo más largo que la columna debe cortar en 400 (no llegar a SaveChanges → 500).
    [Fact]
    public void Categoria_mas_larga_que_la_columna_es_invalida()
    {
        var comando = Valido() with { CategoriaMotivo = new string('x', 81) }; // columna nvarchar(80)
        Assert.False(_validator.Validate(comando).IsValid);
    }

    [Fact]
    public void Detalle_mas_largo_que_la_columna_es_invalido()
    {
        var comando = Valido() with { DetalleMotivo = new string('x', 1001) }; // columna nvarchar(1000)
        Assert.False(_validator.Validate(comando).IsValid);
    }

    [Fact]
    public void Motivo_en_el_limite_exacto_es_valido()
    {
        var comando = Valido() with { CategoriaMotivo = new string('x', 80), DetalleMotivo = new string('y', 1000) };
        Assert.True(_validator.Validate(comando).IsValid);
    }
}
