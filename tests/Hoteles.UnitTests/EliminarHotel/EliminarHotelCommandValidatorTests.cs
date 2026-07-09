using FluentValidation.TestHelper;
using Hoteles.Application.Hoteles.EliminarHotel;

namespace Hoteles.UnitTests.EliminarHotel;

public sealed class EliminarHotelCommandValidatorTests
{
    private readonly EliminarHotelCommandValidator _validator = new();

    private static EliminarHotelCommand Valido() => new(Guid.CreateVersion7(), [1, 2, 3, 4]);

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

    [Fact]
    public void RowVersion_vacio_es_invalido()
    {
        _validator.TestValidate(Valido() with { RowVersion = [] })
            .ShouldHaveValidationErrorFor(c => c.RowVersion);
    }
}
