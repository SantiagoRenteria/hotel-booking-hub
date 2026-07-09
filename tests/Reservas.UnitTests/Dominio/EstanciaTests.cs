using Reservas.Domain.Reservas;
using Xunit;

namespace Reservas.UnitTests.Dominio;

public sealed class EstanciaTests
{
    [Fact]
    public void Noches_cuenta_los_dias_del_rango_semiabierto()
    {
        var estancia = Estancia.Crear(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 14));
        Assert.Equal(4, estancia.Noches); // [10,14) = noches del 10, 11, 12, 13
    }

    [Fact]
    public void Una_noche_es_el_minimo_valido()
    {
        var entrada = new DateOnly(2026, 8, 10);
        var estancia = Estancia.Crear(entrada, entrada.AddDays(1)); // borde adyacente al guard
        Assert.Equal(1, estancia.Noches);
    }

    [Theory]
    [InlineData("2026-08-10", "2026-08-10")] // salida == entrada
    [InlineData("2026-08-14", "2026-08-10")] // salida < entrada
    public void Salida_no_posterior_a_entrada_es_invalida(string entrada, string salida)
    {
        var ex = Assert.Throws<EstanciaInvalidaException>(
            () => Estancia.Crear(DateOnly.Parse(entrada), DateOnly.Parse(salida)));

        Assert.Equal("La fecha de salida debe ser posterior a la de entrada", ex.Message);
    }
}
