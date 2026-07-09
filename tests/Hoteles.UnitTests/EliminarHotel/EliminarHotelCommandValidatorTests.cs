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

    // Fallo-cerrado del token (party-mode · Murat): sin rowVersion la baja NUNCA procede — el DELETE viaja con
    // el token en el body; si un proxy lo descarta (body ausente → vacío/null), el validator corta con 400,
    // jamás un borrado sin control de concurrencia. Se prueban ambos: vacío y null.
    [Fact]
    public void RowVersion_vacio_es_invalido()
    {
        _validator.TestValidate(Valido() with { RowVersion = [] })
            .ShouldHaveValidationErrorFor(c => c.RowVersion);
    }

    [Fact]
    public void RowVersion_null_es_invalido()
    {
        _validator.TestValidate(Valido() with { RowVersion = null! })
            .ShouldHaveValidationErrorFor(c => c.RowVersion);
    }
}
