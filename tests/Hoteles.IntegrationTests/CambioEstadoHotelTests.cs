using HotelBookingHub.Comun.Excepciones;
using Hoteles.Domain.Hoteles;
using Hoteles.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hoteles.IntegrationTests;

/// <summary>
/// Transición de estado (habilitar/deshabilitar) contra SQL Server real: persistencia, concurrencia optimista
/// por <c>rowversion</c> y el 404 sobre un hotel eliminado (query filter).
/// </summary>
[Collection("sqlserver-hoteles")]
public sealed class CambioEstadoHotelTests(SqlServerFixture fixture)
{
    // Nombre único por test (xUnit instancia la clase por método): la BD del fixture es COMPARTIDA por la
    // colección y el índice único (agente, nombre, ciudad) rechazaría dos siembras iguales entre tests.
    private readonly string _nombre = $"Hotel Central {Guid.NewGuid():N}";

    private async Task<Guid> CrearHotelAsync()
    {
        await using var db = fixture.CrearContexto();
        var hotel = Hotel.Crear(_nombre, "Medellín", "Calle 1 # 2-3", "Boutique", EstadoHotel.Habilitado, "agente@test.com");
        await new HotelRepository(db).CrearAsync(hotel, CancellationToken.None);
        return hotel.Id;
    }

    private async Task<byte[]> LeerRowVersionAsync(Guid id)
    {
        await using var db = fixture.CrearContexto();
        var hotel = await db.Hoteles.FirstAsync(h => h.Id == id);
        return (byte[])db.Entry(hotel).Property("RowVersion").CurrentValue!;
    }

    // AC-E2.3.1 — deshabilitar persiste Estado=Deshabilitado.
    [Fact]
    public async Task Deshabilitar_persiste_el_estado()
    {
        var id = await CrearHotelAsync();
        var rowVersion = await LeerRowVersionAsync(id);

        await using (var db = fixture.CrearContexto())
        {
            var repo = new HotelRepository(db);
            var hotel = await repo.ObtenerAsync(id, CancellationToken.None);
            hotel!.Deshabilitar();
            await repo.GuardarConcurrenciaAsync(hotel, rowVersion, CancellationToken.None);
        }

        await using (var db = fixture.CrearContexto())
        {
            var hotel = await db.Hoteles.FirstAsync(h => h.Id == id);
            Assert.Equal(EstadoHotel.Deshabilitado, hotel.Estado);
        }
    }

    // AC-E2.3.3 — dos transiciones concurrentes con el mismo rowVersion: 1 confirma, 1 recibe 409.
    [Fact]
    public async Task Dos_transiciones_con_el_mismo_rowversion_una_confirma_la_otra_recibe_409()
    {
        var id = await CrearHotelAsync();
        var rowVersion = await LeerRowVersionAsync(id);

        async Task<string> DeshabilitarAsync()
        {
            await using var db = fixture.CrearContexto();
            var repo = new HotelRepository(db);
            var hotel = await repo.ObtenerAsync(id, CancellationToken.None);
            hotel!.Deshabilitar();
            try
            {
                await repo.GuardarConcurrenciaAsync(hotel, rowVersion, CancellationToken.None);
                return "ok";
            }
            catch (ConflictoConcurrenciaException)
            {
                return "409";
            }
        }

        var resultados = await Task.WhenAll(DeshabilitarAsync(), DeshabilitarAsync());

        Assert.Equal(1, resultados.Count(r => r == "ok"));
        Assert.Equal(1, resultados.Count(r => r == "409"));
    }

    // AC-E2.3.4 — un hotel eliminado no es visible → el handler responde 404 (ObtenerAsync da null).
    [Fact]
    public async Task Cambiar_estado_de_un_hotel_eliminado_da_no_encontrado()
    {
        var id = await CrearHotelAsync();
        var rowVersion = await LeerRowVersionAsync(id);

        await using (var db = fixture.CrearContexto())
        {
            var repo = new HotelRepository(db);
            var hotel = await repo.ObtenerAsync(id, CancellationToken.None);
            hotel!.Eliminar();
            await repo.GuardarConcurrenciaAsync(hotel, rowVersion, CancellationToken.None);
        }

        await using (var db = fixture.CrearContexto())
        {
            Assert.Null(await new HotelRepository(db).ObtenerAsync(id, CancellationToken.None));
        }
    }
}
