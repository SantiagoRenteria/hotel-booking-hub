using FluentValidation.TestHelper;
using Hoteles.Application.Hoteles.CambiarEstadoHotel;
using Hoteles.Domain.Hoteles;

namespace Hoteles.UnitTests.CambiarEstadoHotel;

public sealed class CambiarEstadoHotelCommandValidatorTests
{
    private readonly CambiarEstadoHotelCommandValidator _validator = new();

    private static CambiarEstadoHotelCommand Valido() =>
        new(Guid.CreateVersion7(), [1, 2, 3, 4], EstadoHotel.Habilitado);

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

    [Fact]
    public void Estado_objetivo_fuera_de_rango_es_invalido()
    {
        _validator.TestValidate(Valido() with { EstadoObjetivo = (EstadoHotel)99 })
            .ShouldHaveValidationErrorFor(c => c.EstadoObjetivo);
    }
}
