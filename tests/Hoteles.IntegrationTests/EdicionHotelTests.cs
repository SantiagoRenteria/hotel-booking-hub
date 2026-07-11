using HotelBookingHub.Comun.Excepciones;
using Hoteles.Domain.Hoteles;
using Hoteles.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hoteles.IntegrationTests;

/// <summary>
/// Concurrencia optimista y soft delete contra SQL Server real. El motor es la única forma de comprobar que el
/// <c>rowversion</c> arbitra de verdad (un mock no detecta un token mal mapeado ni el last-write-wins silencioso).
/// </summary>
[Collection("sqlserver-hoteles")]
public sealed class EdicionHotelTests(SqlServerFixture fixture)
{
    // Nombre único por test: la BD del fixture es compartida y el índice único (agente, nombre, ciudad) rechaza
    // dos siembras iguales entre tests. Las ediciones cambian el nombre a valores propios de cada test.
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

    // AC-E2.2.3 — dos ediciones con el mismo rowVersion: exactamente 1 confirma, la otra 409 (nunca 500, nunca 2).
    [Fact]
    public async Task Dos_ediciones_con_el_mismo_rowversion_una_confirma_la_otra_recibe_409()
    {
        var id = await CrearHotelAsync();
        var rowVersion = await LeerRowVersionAsync(id);

        async Task<string> EditarAsync(string nombre)
        {
            await using var db = fixture.CrearContexto();
            var repo = new HotelRepository(db);
            var hotel = await repo.ObtenerAsync(id, CancellationToken.None);
            hotel!.Editar(nombre, hotel.Ciudad, hotel.Direccion, hotel.Descripcion);
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

        var resultados = await Task.WhenAll(EditarAsync("Renovado A"), EditarAsync("Renovado B"));

        Assert.Equal(1, resultados.Count(r => r == "ok"));
        Assert.Equal(1, resultados.Count(r => r == "409"));
    }

    // AC-E2.2.1 — edición con el rowVersion vigente: confirma y persiste los datos nuevos. El Estado NO cambia
    // por edición (permanece Habilitado): el ciclo de vida se reserva a la operación dedicada de 2.3.
    [Fact]
    public async Task Edicion_con_rowversion_vigente_persiste_los_cambios()
    {
        var id = await CrearHotelAsync();
        var rowVersion = await LeerRowVersionAsync(id);

        await using (var db = fixture.CrearContexto())
        {
            var repo = new HotelRepository(db);
            var hotel = await repo.ObtenerAsync(id, CancellationToken.None);
            hotel!.Editar("Hotel Renovado", "Cali", hotel.Direccion, hotel.Descripcion);
            await repo.GuardarConcurrenciaAsync(hotel, rowVersion, CancellationToken.None);
        }

        await using (var db = fixture.CrearContexto())
        {
            var hotel = await db.Hoteles.FirstAsync(h => h.Id == id);
            Assert.Equal("Hotel Renovado", hotel.Nombre);
            Assert.Equal("Cali", hotel.Ciudad);
            Assert.Equal(EstadoHotel.Habilitado, hotel.Estado); // invariante: la edición no toca el estado
        }
    }

    // AC-E2.2.3 (borde no-op) — una edición con rowVersion obsoleto debe ser 409 AUNQUE no cambie ningún valor:
    // sin forzar el UPDATE, EF no emitiría sentencia para una edición idempotente y no comprobaría el token
    // (devolvería 200 en silencio). Este test falla si se retira el `State = Modified` del repositorio.
    [Fact]
    public async Task Editar_con_rowversion_obsoleto_es_409_aunque_los_valores_no_cambien()
    {
        var id = await CrearHotelAsync();
        var rowVersionObsoleto = await LeerRowVersionAsync(id);

        // Alguien guarda primero → el rowversion avanza.
        await using (var db = fixture.CrearContexto())
        {
            var repo = new HotelRepository(db);
            var hotel = await repo.ObtenerAsync(id, CancellationToken.None);
            await repo.GuardarConcurrenciaAsync(hotel!, rowVersionObsoleto, CancellationToken.None);
        }

        // Un cliente con el rowversion viejo reenvía los MISMOS valores (edición idempotente) → debe ser 409.
        await using (var db = fixture.CrearContexto())
        {
            var repo = new HotelRepository(db);
            var hotel = await repo.ObtenerAsync(id, CancellationToken.None);
            await Assert.ThrowsAsync<ConflictoConcurrenciaException>(
                () => repo.GuardarConcurrenciaAsync(hotel!, rowVersionObsoleto, CancellationToken.None));
        }
    }

    // AC-E2.2.2 — la baja lógica oculta el hotel del query filter, pero la fila sigue físicamente (sin DELETE).
    [Fact]
    public async Task Eliminar_oculta_el_hotel_del_query_filter_sin_borrado_fisico()
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
            // AC-E2.2.4 — «no encontrado» de estado previo: entrar a operar sobre un hotel ya eliminado da null (404).
            Assert.Null(await new HotelRepository(db).ObtenerAsync(id, CancellationToken.None));
            Assert.Equal(1, await db.Hoteles.IgnoreQueryFilters().CountAsync(h => h.Id == id));
        }
    }

    // Borde (Murat) — una edición que ya está en curso y pierde contra un borrado concurrente es 409, NO 404:
    // el 404 solo aplica cuando el hotel ya estaba invisible ANTES de entrar a operar.
    [Fact]
    public async Task Editar_perdiendo_contra_un_borrado_concurrente_es_409_no_404()
    {
        var id = await CrearHotelAsync();
        var rowVersion = await LeerRowVersionAsync(id);

        await using var dbEdicion = fixture.CrearContexto();
        var repoEdicion = new HotelRepository(dbEdicion);
        var hotelEnEdicion = await repoEdicion.ObtenerAsync(id, CancellationToken.None);
        hotelEnEdicion!.Editar("Renovado", hotelEnEdicion.Ciudad, hotelEnEdicion.Direccion, hotelEnEdicion.Descripcion);

        // Otro agente elimina en medio (bumpea el rowVersion).
        await using (var dbBorrado = fixture.CrearContexto())
        {
            var repoBorrado = new HotelRepository(dbBorrado);
            var hotelABorrar = await repoBorrado.ObtenerAsync(id, CancellationToken.None);
            hotelABorrar!.Eliminar();
            await repoBorrado.GuardarConcurrenciaAsync(hotelABorrar, rowVersion, CancellationToken.None);
        }

        await Assert.ThrowsAsync<ConflictoConcurrenciaException>(
            () => repoEdicion.GuardarConcurrenciaAsync(hotelEnEdicion, rowVersion, CancellationToken.None));
    }
}
