using System.Globalization;
using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;
using Xunit;

namespace Reservas.UnitTests.Dominio;

public sealed class CalculadorPrecioTests
{
    private readonly CalculadorPrecio _calculador = new();

    // Los importes van como string y se parsean a decimal: evita la trampa de literales double en
    // [InlineData] (p. ej. 19.99 no es exacto en double) y ejerce de verdad el tipo decimal.
    [Theory]
    [InlineData("100.00", "19.00", 3, "357.00")]  // (100 + 19) × 3
    [InlineData("80.00", "0.00", 1, "80.00")]      // impuesto 0, una noche
    [InlineData("100.50", "19.99", 2, "240.98")]   // decimales no exactos: (100.50 + 19.99) × 2
    public void Precio_total_es_costoBase_mas_impuesto_por_noches(
        string costoBase, string impuesto, int noches, string esperado)
    {
        var entrada = new DateOnly(2026, 8, 10);
        var estancia = Estancia.Crear(entrada, entrada.AddDays(noches));

        var total = _calculador.Calcular(Dec(costoBase), Dec(impuesto), estancia);

        Assert.Equal(Dec(esperado), total);
    }

    [Theory]
    [InlineData("-0.01", "0")]   // costoBase negativo
    [InlineData("0", "-0.01")]   // impuesto negativo
    public void Rechaza_dinero_negativo(string costoBase, string impuesto)
    {
        var entrada = new DateOnly(2026, 8, 10);
        var estancia = Estancia.Crear(entrada, entrada.AddDays(1));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _calculador.Calcular(Dec(costoBase), Dec(impuesto), estancia));
    }

    private static decimal Dec(string valor) => decimal.Parse(valor, CultureInfo.InvariantCulture);
}
