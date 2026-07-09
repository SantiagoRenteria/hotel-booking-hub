using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;
using Xunit;

namespace Reservas.UnitTests.Dominio;

/// <summary>
/// Story 4.1 (AC-E4.1.1) — regla de la penalidad SUGERIDA con foco en los BORDES (Murat): el umbral es
/// <c>&gt;=30</c> días a la entrada → <c>0%</c>; <c>&lt;30</c> → <c>100%</c>. El servicio es puro: la
/// antelación se mide como <c>entrada - fechaSolicitud</c> en días (sin reloj), 100% determinista.
/// </summary>
public sealed class CalculadorPenalidadTests
{
    private readonly CalculadorPenalidad _calculador = new();

    // La antelación (días entre la fecha de solicitud y la entrada) es lo único que decide el porcentaje.
    [Theory]
    [InlineData(31, 0)]   // muy anticipado → sin penalidad
    [InlineData(30, 0)]   // BORDE exacto: 30 días justos → 0% (>=30)
    [InlineData(29, 100)] // BORDE: un día menos que el umbral → 100%
    [InlineData(10, 100)] // pocos días → 100%
    [InlineData(1, 100)]  // víspera → 100%
    public void Porcentaje_depende_de_la_antelacion_a_la_entrada(int diasDeAntelacion, int porcentajeEsperado)
    {
        var entrada = new DateOnly(2026, 9, 1);
        var fechaSolicitud = entrada.AddDays(-diasDeAntelacion);
        var estancia = Estancia.Crear(entrada, entrada.AddDays(1));

        var penalidad = _calculador.Calcular(estancia, fechaSolicitud);

        Assert.Equal(porcentajeEsperado, penalidad.Porcentaje);
    }

    [Fact]
    public void El_umbral_sin_penalidad_es_treinta_dias()
    {
        Assert.Equal(30, CalculadorPenalidad.DiasUmbralSinPenalidad);
    }
}
