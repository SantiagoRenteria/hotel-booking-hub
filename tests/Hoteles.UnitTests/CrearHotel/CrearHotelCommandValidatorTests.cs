using FluentValidation.TestHelper;
using Hoteles.Application.Hoteles.CrearHotel;
using Hoteles.Domain.Hoteles;

namespace Hoteles.UnitTests.CrearHotel;

public sealed class CrearHotelCommandValidatorTests
{
    private readonly CrearHotelCommandValidator _validator = new();

    private static CrearHotelCommand Valido() =>
        new("Hotel Central", "Medellín", "Calle 1 # 2-3", "Boutique en el centro", EstadoHotel.Habilitado);

    [Fact]
    public void Comando_valido_no_tiene_errores()
    {
        _validator.TestValidate(Valido()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Nombre_vacio_es_invalido()
    {
        _validator.TestValidate(Valido() with { Nombre = "" })
            .ShouldHaveValidationErrorFor(c => c.Nombre);
    }

    [Fact]
    public void Ciudad_vacia_es_invalida()
    {
        _validator.TestValidate(Valido() with { Ciudad = "" })
            .ShouldHaveValidationErrorFor(c => c.Ciudad);
    }

    [Fact]
    public void Estado_fuera_de_rango_es_invalido()
    {
        _validator.TestValidate(Valido() with { Estado = (EstadoHotel)99 })
            .ShouldHaveValidationErrorFor(c => c.Estado);
    }

    // Un nombre sobredimensionado debe rechazarse como 400 (validación), no reventar en el INSERT (500).
    [Fact]
    public void Nombre_que_excede_la_longitud_maxima_es_invalido()
    {
        _validator.TestValidate(Valido() with { Nombre = new string('a', 201) })
            .ShouldHaveValidationErrorFor(c => c.Nombre);
    }
}
