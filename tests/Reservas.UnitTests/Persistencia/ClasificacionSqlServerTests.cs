using Reservas.Infrastructure.Persistencia;
using Xunit;

namespace Reservas.UnitTests.Persistencia;

public sealed class ClasificacionSqlServerTests
{
    [Theory]
    [InlineData(2627)] // PRIMARY KEY / UNIQUE constraint
    [InlineData(2601)] // unique index
    public void Violacion_de_unico_se_reconoce_y_no_es_deadlock(int number)
    {
        Assert.True(ClasificacionSqlServer.EsViolacionDeUnico(number));
        Assert.False(ClasificacionSqlServer.EsDeadlock(number));
    }

    [Fact]
    public void Deadlock_1205_se_reconoce_y_no_es_violacion_de_unico()
    {
        Assert.True(ClasificacionSqlServer.EsDeadlock(1205));
        Assert.False(ClasificacionSqlServer.EsViolacionDeUnico(1205));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50000)]
    public void Otros_numeros_no_se_clasifican(int number)
    {
        Assert.False(ClasificacionSqlServer.EsViolacionDeUnico(number));
        Assert.False(ClasificacionSqlServer.EsDeadlock(number));
    }
}
