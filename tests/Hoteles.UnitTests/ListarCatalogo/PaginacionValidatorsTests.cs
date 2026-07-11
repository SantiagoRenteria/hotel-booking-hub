using FluentValidation.TestHelper;
using Hoteles.Application.Habitaciones.ListarHabitaciones;
using Hoteles.Application.Hoteles.ListarHoteles;

namespace Hoteles.UnitTests.ListarCatalogo;

/// <summary>
/// Story T.6 (AC-ET.6.1/.2) — la paginación de las listas del catálogo se valida (→ 400 vía ValidationBehavior).
/// Cota `page ≥ 1` y `pageSize ∈ [1,100]` (evita OFFSET negativo y páginas enormes que tumben el servicio).
/// </summary>
public sealed class PaginacionValidatorsTests
{
    private readonly ListarHotelesDelAgenteQueryValidator _hoteles = new();
    private readonly ListarHabitacionesDeHotelQueryValidator _habitaciones = new();

    [Theory]
    [InlineData(1, 20)]
    [InlineData(5, 1)]
    [InlineData(2, 100)]
    public void Hoteles_paginacion_valida_no_tiene_errores(int page, int pageSize)
    {
        _hoteles.TestValidate(new ListarHotelesDelAgenteQuery(page, pageSize)).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Hoteles_page_menor_que_1_es_invalido(int page)
    {
        _hoteles.TestValidate(new ListarHotelesDelAgenteQuery(page, 20))
            .ShouldHaveValidationErrorFor(q => q.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Hoteles_pageSize_fuera_de_rango_es_invalido(int pageSize)
    {
        _hoteles.TestValidate(new ListarHotelesDelAgenteQuery(1, pageSize))
            .ShouldHaveValidationErrorFor(q => q.PageSize);
    }

    [Fact]
    public void Habitaciones_paginacion_valida_no_tiene_errores()
    {
        _habitaciones.TestValidate(new ListarHabitacionesDeHotelQuery(Guid.NewGuid(), 1, 20))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Habitaciones_paginacion_fuera_de_rango_es_invalida(int page, int pageSize)
    {
        var resultado = _habitaciones.TestValidate(new ListarHabitacionesDeHotelQuery(Guid.NewGuid(), page, pageSize));
        Assert.False(resultado.IsValid);
    }
}
