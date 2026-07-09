using Reservas.Infrastructure.Proyeccion;
using Xunit;

namespace Reservas.UnitTests.Proyeccion;

/// <summary>
/// Story 3.1 (AC-E3.1.1/2) — VITRINA de convergencia distribuida (BDD). La <see cref="ProyeccionHabitacion"/>
/// es un CRDT de tres zonas con field-ownership (party-mode C): estáticos write-once (dueño: HabitacionAgregada),
/// precio y estado con LWW por versión de bloque. Así converge al estado correcto bajo REENTREGA y DESORDEN
/// (incluido un delta que llega ANTES del create), y ningún evento viejo retrocede el estado. Determinista:
/// se aplican los eventos en el orden que fija cada escenario, sin concurrencia ni tiempo.
/// </summary>
public sealed class ProyeccionHabitacionConvergenciaTests
{
    private static readonly Guid _hab = Guid.Parse("019f43ef-0000-7000-8000-000000000a01");
    private static readonly Guid _hotel = Guid.Parse("019f43ef-0000-7000-8000-000000000b01");

    private static ProyeccionHabitacion NuevaVacia() => ProyeccionHabitacion.Nueva(_hab);

    private static void Agregada(ProyeccionHabitacion p, int version, decimal costo, string estado = "Habilitada") =>
        p.AplicarAgregada(_hotel, "Bogotá", "Suite", "Piso 3", capacidad: 2, costoBase: costo, impuestos: 19m, estado: estado, version: version);

    // Escenario 1 (orden feliz): alta → precio → deshabilitar → estado final coherente.
    [Fact]
    public void Orden_feliz_converge_al_ultimo_estado()
    {
        var p = NuevaVacia();
        Agregada(p, version: 1, costo: 100m);
        p.AplicarPrecio(150m, 28.5m, version: 2);
        p.AplicarDeshabilitada(version: 3);

        Assert.True(p.Hidratada);
        Assert.Equal(150m, p.CostoBase);
        Assert.Equal("Deshabilitada", p.Estado);
        Assert.Equal("Bogotá", p.Ciudad);
    }

    // Escenario 2 (convergencia bajo desorden — el AC estrella): el precio (v2) llega ANTES del alta (v1).
    // Converge a precio v2 + catálogo del alta; la fila queda hidratada.
    [Fact]
    public void Precio_antes_del_alta_converge_a_precio_nuevo_con_catalogo_completo()
    {
        var p = NuevaVacia();
        p.AplicarPrecio(200m, 30m, version: 2); // llega primero, sin catálogo
        Assert.False(p.Hidratada);               // fila parcial (estáticos NULL)

        Agregada(p, version: 1, costo: 100m);     // alta tardía

        Assert.True(p.Hidratada);                 // ya tiene catálogo
        Assert.Equal("Suite", p.Tipo);            // catálogo del alta
        Assert.Equal(200m, p.CostoBase);          // precio v2 gana (no lo pisa el alta v1)
        Assert.Equal(30m, p.Impuestos);
    }

    // Escenario 3 (no-regresión): un alta tardía (redelivery) no retrocede un precio más nuevo.
    [Fact]
    public void Alta_tardia_no_retrocede_un_precio_mas_nuevo()
    {
        var p = NuevaVacia();
        Agregada(p, version: 1, costo: 100m);
        p.AplicarPrecio(200m, 30m, version: 2);

        Agregada(p, version: 1, costo: 100m); // redelivery del alta

        Assert.Equal(200m, p.CostoBase); // sigue v2, no vuelve a 100
    }

    // Escenario 4 (idempotencia): aplicar el mismo alta N veces deja el estado idéntico (estáticos write-once).
    [Fact]
    public void Alta_repetida_es_idempotente()
    {
        var p = NuevaVacia();
        Agregada(p, version: 1, costo: 100m);
        Agregada(p, version: 1, costo: 100m);
        Agregada(p, version: 1, costo: 100m);

        Assert.Equal(1, p.VersionEstatico);
        Assert.Equal(100m, p.CostoBase);
        Assert.Equal("Suite", p.Tipo);
    }

    // Escenario 5 (deshabilitada no revivible): tras deshabilitar (v3), un alta tardía (v1) no revive el estado.
    [Fact]
    public void Deshabilitada_no_revive_por_un_alta_tardia()
    {
        var p = NuevaVacia();
        p.AplicarDeshabilitada(version: 3);

        Agregada(p, version: 1, costo: 100m, estado: "Habilitada"); // alta tardía con estado habilitada

        Assert.Equal("Deshabilitada", p.Estado); // v3 > v1 → no revive
    }

    // AC-E3.1.1 Scenario Outline — cualquier orden de llegada de {alta v1, precio v2, deshabilitar v3} converge
    // al MISMO estado final (precio v2, estado v3, catálogo del alta). La convergencia es order-independent.
    [Theory]
    [InlineData("1,2,3")]
    [InlineData("3,1,2")]
    [InlineData("2,3,1")]
    [InlineData("3,2,1")]
    [InlineData("2,1,3")]
    public void Cualquier_orden_de_llegada_converge_al_mismo_estado_final(string orden)
    {
        var p = NuevaVacia();
        foreach (var n in orden.Split(','))
        {
            switch (n)
            {
                case "1": Agregada(p, version: 1, costo: 100m); break;      // alta (Habilitada, precio 100)
                case "2": p.AplicarPrecio(150m, 28.5m, version: 2); break;  // cambio de precio
                case "3": p.AplicarDeshabilitada(version: 3); break;         // deshabilitar
            }
        }

        Assert.True(p.Hidratada);
        Assert.Equal("Bogotá", p.Ciudad);          // catálogo del alta
        Assert.Equal(150m, p.CostoBase);           // precio v2 gana
        Assert.Equal("Deshabilitada", p.Estado);   // estado v3 gana
    }

    // Escenario 6 (delta viejo descartado): un PrecioCambiado con versión menor no pisa el precio vigente.
    [Fact]
    public void Precio_con_version_vieja_se_descarta()
    {
        var p = NuevaVacia();
        Agregada(p, version: 1, costo: 100m);
        p.AplicarPrecio(300m, 40m, version: 3);
        p.AplicarPrecio(200m, 30m, version: 2); // llega tarde, versión menor

        Assert.Equal(300m, p.CostoBase); // sigue v3
    }
}
