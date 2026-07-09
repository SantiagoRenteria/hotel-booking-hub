using FluentValidation.TestHelper;
using Reservas.Application.Reservas.BuscarDisponibilidad;

namespace Reservas.UnitTests.BuscarDisponibilidad;

/// <summary>Validación de entrada de la búsqueda (AC-E3.2.1): ciudad, huéspedes y rango semiabierto coherente.</summary>
public sealed class BuscarDisponibilidadQueryValidatorTests
{
    private readonly BuscarDisponibilidadQueryValidator _validator = new();

    private static BuscarDisponibilidadQuery Valida() =>
        new("Bogotá", new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 12), 2);

    [Fact]
    public void Consulta_valida_no_tiene_errores()
    {
        var resultado = _validator.TestValidate(Valida());
        resultado.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ciudad_vacia_es_invalida(string ciudad)
    {
        var resultado = _validator.TestValidate(Valida() with { Ciudad = ciudad });
        resultado.ShouldHaveValidationErrorFor(c => c.Ciudad);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Huespedes_menor_que_uno_es_invalido(int huespedes)
    {
        var resultado = _validator.TestValidate(Valida() with { Huespedes = huespedes });
        resultado.ShouldHaveValidationErrorFor(c => c.Huespedes);
    }

    [Fact]
    public void Salida_no_posterior_a_entrada_es_invalida()
    {
        var resultado = _validator.TestValidate(Valida() with
        {
            Entrada = new DateOnly(2026, 8, 12),
            Salida = new DateOnly(2026, 8, 12),
        });
        resultado.ShouldHaveValidationErrorFor(c => c.Salida);
    }

    [Fact]
    public void Rango_que_excede_el_tope_de_noches_es_invalido()
    {
        var resultado = _validator.TestValidate(Valida() with
        {
            Entrada = new DateOnly(2026, 1, 1),
            Salida = new DateOnly(2999, 1, 1),
        });
        resultado.ShouldHaveValidationErrorFor(c => c.Salida);
    }
}
