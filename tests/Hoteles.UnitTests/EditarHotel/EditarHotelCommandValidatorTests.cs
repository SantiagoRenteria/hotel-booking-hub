using FluentValidation.TestHelper;
using Hoteles.Application.Hoteles.EditarHotel;
using Hoteles.Domain.Hoteles;

namespace Hoteles.UnitTests.EditarHotel;

public sealed class EditarHotelCommandValidatorTests
{
    private readonly EditarHotelCommandValidator _validator = new();

    private static EditarHotelCommand Valido() =>
        new(Guid.CreateVersion7(), [1, 2, 3, 4], "Hotel Central", "Medellín", "Calle 1", "Boutique");

    [Fact]
    public void Comando_valido_no_tiene_errores()
    {
        _validator.TestValidate(Valido()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Id_vacio_es_invalido()
    {
        _validator.TestValidate(Valido() with { Id = Guid.Empty })
            .ShouldHaveValidationErrorFor(c => c.Id);
    }

    // La concurrencia optimista exige el rowVersion: sin él no se puede arbitrar la carrera.
    [Fact]
    public void RowVersion_vacio_es_invalido()
    {
        _validator.TestValidate(Valido() with { RowVersion = [] })
            .ShouldHaveValidationErrorFor(c => c.RowVersion);
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

    // Un valor sobredimensionado debe ser 400 (validación), no reventar en el UPDATE (500 por truncamiento).
    [Fact]
    public void Nombre_que_excede_la_longitud_maxima_es_invalido()
    {
        _validator.TestValidate(Valido() with { Nombre = new string('a', LongitudesHotel.Nombre + 1) })
            .ShouldHaveValidationErrorFor(c => c.Nombre);
    }
}
