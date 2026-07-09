using HotelBookingHub.Comun.Excepciones;
using HotelBookingHub.Comun.Resultados;
using Reservas.Domain.Reservas;

namespace Reservas.UnitTests.Dominio;

/// <summary>
/// Contrato del subtipo: el overbooking es una <see cref="ExcepcionNegocio"/> con semántica
/// <see cref="EstadoResultado.Conflicto"/>. Combinado con la batería del handler transversal
/// (Comun.Web.UnitTests, que prueba Conflicto→409), garantiza el 409 de overbooking sin acoplar el
/// test transversal a este BC.
/// </summary>
public sealed class HabitacionNoDisponibleExceptionTests
{
    [Fact]
    public void Overbooking_porta_estado_conflicto()
    {
        Assert.Equal(EstadoResultado.Conflicto, new HabitacionNoDisponibleException().Estado);
    }
}
