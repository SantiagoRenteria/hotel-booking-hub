using Hoteles.Domain.Habitaciones;
using Xunit;

namespace Hoteles.UnitTests.Habitaciones;

/// <summary>
/// Story 2.5 (AC-E2.5.4) — la <c>Version</c> del agregado es la parte monotónica del order key
/// <c>(aggregateId, version)</c> y cuenta SOLO las mutaciones que emiten evento: alta (1), cambio real de
/// precio y deshabilitación. Las mutaciones que NO emiten (editar sin cambiar precio, habilitar, idempotentes)
/// NO la incrementan → la secuencia de eventos emitidos es estrictamente creciente y contigua, sin huecos.
/// </summary>
public sealed class HabitacionVersionTests
{
    private static Habitacion Nueva() =>
        Habitacion.Crear(Guid.CreateVersion7(), "Suite", 100m, 19m, "Piso 3", EstadoHabitacion.Habilitada, capacidad: 2);

    [Fact]
    public void Al_crear_la_version_es_1()
    {
        Assert.Equal(1, Nueva().Version);
    }

    [Fact]
    public void Editar_cambiando_el_precio_incrementa_la_version_y_reporta_el_cambio()
    {
        var h = Nueva();

        var cambioPrecio = h.Editar("Suite Premium", 150m, 28.5m, "Piso 5", capacidad: 2);

        Assert.True(cambioPrecio);
        Assert.Equal(2, h.Version);
    }

    [Fact]
    public void Editar_sin_cambiar_el_precio_no_incrementa_la_version_ni_reporta_cambio()
    {
        var h = Nueva();

        // Cambia datos NO económicos (tipo, ubicación) pero deja costoBase/impuestos idénticos.
        var cambioPrecio = h.Editar("Suite Deluxe", 100m, 19m, "Piso 9", capacidad: 2);

        Assert.False(cambioPrecio);
        Assert.Equal(1, h.Version);
    }

    [Fact]
    public void Deshabilitar_incrementa_la_version_una_vez_y_es_idempotente()
    {
        var h = Nueva();

        var primera = h.Deshabilitar();
        var segunda = h.Deshabilitar(); // idempotente: ya está deshabilitada

        Assert.True(primera);
        Assert.False(segunda);
        Assert.Equal(2, h.Version); // solo el primer deshabilitar contó
    }

    [Fact]
    public void Habilitar_no_emite_evento_por_lo_que_no_incrementa_la_version()
    {
        var h = Nueva();
        h.Deshabilitar(); // Version = 2

        var cambio = h.Habilitar();

        Assert.True(cambio); // sí cambió el estado...
        Assert.Equal(2, h.Version); // ...pero habilitar NO emite (AC-E2.5.3) → sin bump de version
    }

    [Fact]
    public void Mutaciones_emisoras_consecutivas_dan_versions_estrictamente_crecientes_y_contiguas()
    {
        var h = Nueva(); // v1 (alta)

        h.Editar("Suite", 120m, 20m, "Piso 3", capacidad: 2); // v2 (precio)
        var vTrasPrecio = h.Version;
        h.Editar("Suite", 120m, 20m, "Piso 4", capacidad: 2); // sin cambio de precio → no cuenta
        var vTrasNoPrecio = h.Version;
        h.Deshabilitar(); // v3
        var vTrasDeshabilitar = h.Version;

        Assert.Equal(2, vTrasPrecio);
        Assert.Equal(2, vTrasNoPrecio); // no incrementó
        Assert.Equal(3, vTrasDeshabilitar);
    }
}
