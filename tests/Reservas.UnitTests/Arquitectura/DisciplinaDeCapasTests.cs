using NetArchTest.Rules;
using Xunit;

namespace Reservas.UnitTests.Arquitectura;

/// <summary>
/// Verifica la disciplina de Clean Architecture con NetArchTest (AC-E1.1.2):
/// el dominio no debe conocer detalles de infraestructura.
/// </summary>
public sealed class DisciplinaDeCapasTests
{
    [Fact]
    public void Dominio_de_Reservas_no_depende_de_EntityFrameworkCore()
    {
        var resultado = Types
            .InAssembly(typeof(Reservas.Domain.AssemblyReference).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(
            resultado.IsSuccessful,
            "Reservas.Domain no puede depender de EF Core: la persistencia vive en Reservas.Infrastructure (dependencias hacia adentro).");
    }
}
