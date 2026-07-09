using HotelBookingHub.Comun.Excepciones;
using Hoteles.Domain.Habitaciones;
using Hoteles.Domain.Hoteles;
using Hoteles.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hoteles.IntegrationTests;

/// <summary>
/// Habitaciones contra SQL Server real: alta/persistencia, independencia hotel-habitación (editar la habitación
/// no altera el hotel) y concurrencia optimista por <c>rowversion</c>.
/// </summary>
[Collection("sqlserver-hoteles")]
public sealed class HabitacionTests(SqlServerFixture fixture)
{
    private async Task<Guid> CrearHotelAsync()
    {
        await using var db = fixture.CrearContexto();
        var hotel = Hotel.Crear("Hotel Central", "Medellín", "Calle 1", "Boutique", EstadoHotel.Habilitado);
        await new HotelRepository(db).CrearAsync(hotel, CancellationToken.None);
        return hotel.Id;
    }

    private async Task<(Guid Id, byte[] RowVersion)> CrearHabitacionAsync(Guid hotelId)
    {
        await using var db = fixture.CrearContexto();
        var habitacion = Habitacion.Crear(hotelId, "Suite", 100m, 19m, "Piso 3", EstadoHabitacion.Habilitada);
        var rv = await new HabitacionRepository(db).CrearAsync(habitacion, CancellationToken.None);
        return (habitacion.Id, rv);
    }

    // AC-E2.4.1 — la habitación se persiste con su hotel y datos de catálogo.
    [Fact]
    public async Task Crear_persiste_la_habitacion()
    {
        var hotelId = await CrearHotelAsync();
        var (id, _) = await CrearHabitacionAsync(hotelId);

        await using var db = fixture.CrearContexto();
        var habitacion = await db.Habitaciones.FirstAsync(h => h.Id == id);
        Assert.Equal(hotelId, habitacion.HotelId);
        Assert.Equal("Suite", habitacion.Tipo);
        Assert.Equal(100m, habitacion.CostoBase);
        Assert.Equal(EstadoHabitacion.Habilitada, habitacion.Estado);
    }

    // AC-E2.4.2 — editar la habitación NO altera el hotel (independencia de aggregates).
    [Fact]
    public async Task Editar_habitacion_no_altera_el_hotel()
    {
        var hotelId = await CrearHotelAsync();
        var (habId, habRowVersion) = await CrearHabitacionAsync(hotelId);

        byte[] hotelRowVersionAntes;
        await using (var db = fixture.CrearContexto())
        {
            var hotel = await db.Hoteles.FirstAsync(h => h.Id == hotelId);
            hotelRowVersionAntes = (byte[])db.Entry(hotel).Property("RowVersion").CurrentValue!;
        }

        await using (var db = fixture.CrearContexto())
        {
            var repo = new HabitacionRepository(db);
            var hab = await repo.ObtenerAsync(habId, CancellationToken.None);
            hab!.Editar("Suite Premium", 150m, 28.5m, "Piso 5");
            await repo.GuardarConcurrenciaAsync(hab, habRowVersion, CancellationToken.None);
        }

        await using (var db = fixture.CrearContexto())
        {
            var hotel = await db.Hoteles.FirstAsync(h => h.Id == hotelId);
            var hotelRowVersionDespues = (byte[])db.Entry(hotel).Property("RowVersion").CurrentValue!;
            Assert.Equal(hotelRowVersionAntes, hotelRowVersionDespues); // el hotel no se tocó
            Assert.Equal("Hotel Central", hotel.Nombre);

            var hab = await db.Habitaciones.FirstAsync(h => h.Id == habId);
            Assert.Equal("Suite Premium", hab.Tipo); // la habitación sí cambió
        }
    }

    // AC-E2.4.3 — deshabilitar la habitación persiste Estado=Deshabilitada.
    [Fact]
    public async Task Deshabilitar_persiste_el_estado()
    {
        var hotelId = await CrearHotelAsync();
        var (habId, rowVersion) = await CrearHabitacionAsync(hotelId);

        await using (var db = fixture.CrearContexto())
        {
            var repo = new HabitacionRepository(db);
            var hab = await repo.ObtenerAsync(habId, CancellationToken.None);
            hab!.Deshabilitar();
            await repo.GuardarConcurrenciaAsync(hab, rowVersion, CancellationToken.None);
        }

        await using (var db = fixture.CrearContexto())
        {
            var hab = await db.Habitaciones.FirstAsync(h => h.Id == habId);
            Assert.Equal(EstadoHabitacion.Deshabilitada, hab.Estado);
        }
    }

    // AC-E2.4.4 — dos ediciones concurrentes con el mismo rowVersion: 1 confirma, 1 recibe 409.
    [Fact]
    public async Task Dos_ediciones_con_el_mismo_rowversion_una_confirma_la_otra_recibe_409()
    {
        var hotelId = await CrearHotelAsync();
        var (habId, rowVersion) = await CrearHabitacionAsync(hotelId);

        async Task<string> EditarAsync(string tipo)
        {
            await using var db = fixture.CrearContexto();
            var repo = new HabitacionRepository(db);
            var hab = await repo.ObtenerAsync(habId, CancellationToken.None);
            hab!.Editar(tipo, hab.CostoBase, hab.Impuestos, hab.Ubicacion);
            try
            {
                await repo.GuardarConcurrenciaAsync(hab, rowVersion, CancellationToken.None);
                return "ok";
            }
            catch (ConflictoConcurrenciaException)
            {
                return "409";
            }
        }

        var resultados = await Task.WhenAll(EditarAsync("A"), EditarAsync("B"));

        Assert.Equal(1, resultados.Count(r => r == "ok"));
        Assert.Equal(1, resultados.Count(r => r == "409"));
    }
}
