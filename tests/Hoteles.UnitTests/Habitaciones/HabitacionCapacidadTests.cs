using Hoteles.Domain.Habitaciones;
using Xunit;

namespace Hoteles.UnitTests.Habitaciones;

/// <summary>
/// Épica 3 (cierre del hueco de contrato, party-mode A2): la habitación modela su <c>Capacidad</c> (nº de
/// huéspedes) como atributo de catálogo obligatorio y positivo. La búsqueda de disponibilidad (3.2) filtra por
/// <c>capacidad &gt;= huéspedes</c>, así que un valor inválido no debe poder persistirse. La capacidad viaja como
/// ESTADO (no emite evento propio): editarla no bumpea la <c>Version</c> por sí sola.
/// </summary>
public sealed class HabitacionCapacidadTests
{
    private static Habitacion Nueva(int capacidad = 2) =>
        Habitacion.Crear(Guid.CreateVersion7(), "Suite", 100m, 19m, "Piso 3", EstadoHabitacion.Habilitada, capacidad);

    [Fact]
    public void Al_crear_se_fija_la_capacidad()
    {
        Assert.Equal(4, Nueva(4).Capacidad);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Crear_con_capacidad_no_positiva_falla(int capacidad)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Nueva(capacidad));
    }

    [Fact]
    public void Editar_actualiza_la_capacidad()
    {
        var h = Nueva(2);

        h.Editar("Suite", 100m, 19m, "Piso 3", capacidad: 6);

        Assert.Equal(6, h.Capacidad);
    }

    [Fact]
    public void Editar_solo_la_capacidad_no_bumpea_la_version()
    {
        var h = Nueva(2); // Version = 1

        var cambioPrecio = h.Editar("Suite", 100m, 19m, "Piso 3", capacidad: 6); // mismo precio, otra capacidad

        Assert.False(cambioPrecio);
        Assert.Equal(1, h.Version); // la capacidad no emite evento → no versiona
    }

    [Fact]
    public void Editar_con_capacidad_no_positiva_falla()
    {
        var h = Nueva(2);

        Assert.Throws<ArgumentOutOfRangeException>(() => h.Editar("Suite", 100m, 19m, "Piso 3", capacidad: 0));
    }
}
