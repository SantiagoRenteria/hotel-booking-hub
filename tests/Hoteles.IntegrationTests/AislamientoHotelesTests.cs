using Hoteles.Domain.Hoteles;
using Hoteles.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hoteles.IntegrationTests;

/// <summary>
/// Story 6.3 · AC-E6.3.1/.2 — aislamiento entre agentes en Hoteles, sobre SQL Server real (el query filter por
/// propietario solo se puede verificar contra el motor). Un hotel de un agente es invisible para otro → toda
/// carga-para-mutar (editar/eliminar/estado + habitaciones) devuelve 404 y el recurso no cambia. El dueño SÍ lo ve.
/// </summary>
[Collection("sqlserver-hoteles")]
public sealed class AislamientoHotelesTests(SqlServerFixture fixture)
{
    private const string AgenteA = "a@agencia.com";
    private const string AgenteB = "b@agencia.com";

    private async Task<Guid> SembrarHotelDeAsync(string dueno)
    {
        await using var db = fixture.CrearContexto(); // sin identidad → filtro inactivo para sembrar
        var hotel = Hotel.Crear("Hotel A", "Medellín", "Calle 1", "Boutique", EstadoHotel.Habilitado, dueno);
        await new HotelRepository(db).CrearAsync(hotel, CancellationToken.None);
        return hotel.Id;
    }

    [Fact]
    public async Task Hotel_de_otro_agente_es_invisible_para_lectura()
    {
        var id = await SembrarHotelDeAsync(AgenteA);

        await using var dbB = fixture.CrearContextoConAgente(AgenteB);
        Assert.Null(await new HotelRepository(dbB).ObtenerAsync(id, CancellationToken.None));

        // Control: el dueño SÍ lo ve (el filtro no oculta todo indiscriminadamente).
        await using var dbA = fixture.CrearContextoConAgente(AgenteA);
        Assert.NotNull(await new HotelRepository(dbA).ObtenerAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task Agente_ajeno_no_puede_editar_el_hotel_de_otro_y_no_cambia()
    {
        var id = await SembrarHotelDeAsync(AgenteA);

        // Agente B intenta cargar-para-editar: invisible → null → el handler devolvería 404.
        await using (var dbB = fixture.CrearContextoConAgente(AgenteB))
        {
            var ajeno = await new HotelRepository(dbB).ObtenerAsync(id, CancellationToken.None);
            Assert.Null(ajeno);
        }

        // El hotel de A no cambió (sigue con su nombre original), visto por su dueño.
        await using (var dbA = fixture.CrearContextoConAgente(AgenteA))
        {
            var propio = await new HotelRepository(dbA).ObtenerAsync(id, CancellationToken.None);
            Assert.Equal("Hotel A", propio!.Nombre);
        }
    }

    [Fact]
    public async Task Habitacion_de_hotel_ajeno_es_inalcanzable_para_otro_agente()
    {
        // La habitación se aísla por el propietario de su hotel: B no ve el hotel padre → sus habitaciones
        // quedan fuera de alcance (los handlers de edición/estado verifican la visibilidad del hotel → 404).
        var id = await SembrarHotelDeAsync(AgenteA);

        await using var dbB = fixture.CrearContextoConAgente(AgenteB);
        Assert.Null(await new HotelRepository(dbB).ObtenerAsync(id, CancellationToken.None));

        await using var dbA = fixture.CrearContextoConAgente(AgenteA);
        Assert.NotNull(await new HotelRepository(dbA).ObtenerAsync(id, CancellationToken.None));
    }
}
