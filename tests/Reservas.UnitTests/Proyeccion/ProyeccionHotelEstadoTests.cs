using Reservas.Infrastructure.Proyeccion;
using Xunit;

namespace Reservas.UnitTests.Proyeccion;

/// <summary>
/// Story 3.2 (AC-E3.2.2) — la <see cref="ProyeccionHotelEstado"/> es una dimensión LWW independiente del estado
/// de la habitación: converge bajo desorden/reentrega de <c>HotelHabilitado</c>/<c>HotelDeshabilitado</c> por
/// <see cref="ProyeccionHotelEstado.VersionEstado"/>, sin que un evento viejo retroceda el estado.
/// </summary>
public sealed class ProyeccionHotelEstadoTests
{
    private static readonly Guid _hotel = Guid.Parse("019f43ef-0000-7000-8000-000000000c01");

    [Fact]
    public void Nueva_arranca_activa()
    {
        var p = ProyeccionHotelEstado.Nueva(_hotel);
        Assert.True(p.Activo);
        Assert.Equal(0, p.VersionEstado);
    }

    [Fact]
    public void Deshabilitar_luego_habilitar_en_orden_converge_a_activo()
    {
        var p = ProyeccionHotelEstado.Nueva(_hotel);
        Assert.True(p.AplicarDeshabilitado(version: 1));
        Assert.False(p.Activo);
        Assert.True(p.AplicarHabilitado(version: 2));
        Assert.True(p.Activo);
        Assert.Equal(2, p.VersionEstado);
    }

    // Desorden: el HABILITADO (v2, más nuevo) llega ANTES que el DESHABILITADO (v1, viejo) → gana el nuevo.
    [Fact]
    public void Evento_viejo_no_retrocede_el_estado()
    {
        var p = ProyeccionHotelEstado.Nueva(_hotel);
        Assert.True(p.AplicarHabilitado(version: 2));
        Assert.False(p.AplicarDeshabilitado(version: 1)); // viejo por versión → descartado (no mutó)
        Assert.True(p.Activo);
        Assert.Equal(2, p.VersionEstado);
    }

    // Reentrega: la misma versión aplicada dos veces es no-op idempotente.
    [Fact]
    public void Misma_version_repetida_es_idempotente()
    {
        var p = ProyeccionHotelEstado.Nueva(_hotel);
        Assert.True(p.AplicarDeshabilitado(version: 3));
        Assert.False(p.AplicarDeshabilitado(version: 3)); // repetida → no muta
        Assert.False(p.Activo);
        Assert.Equal(3, p.VersionEstado);
    }
}
