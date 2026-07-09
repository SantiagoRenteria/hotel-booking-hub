using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;
using Xunit;

namespace Reservas.UnitTests.Dominio;

public sealed class CalculadorPrecioTests
{
    private readonly CalculadorPrecio _calculador = new();

    [Theory]
    [InlineData(100.00, 19.00, 3, 357.00)] // (100 + 19) × 3
    [InlineData(80.00, 0.00, 1, 80.00)]    // impuesto 0, una noche
    public void Precio_total_es_costoBase_mas_impuesto_por_noches(
        decimal costoBase, decimal impuesto, int noches, decimal esperado)
    {
        var entrada = new DateOnly(2026, 8, 10);
        var estancia = Estancia.Crear(entrada, entrada.AddDays(noches));

        var total = _calculador.Calcular(costoBase, impuesto, estancia);

        Assert.Equal(esperado, total);
    }
}
