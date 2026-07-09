using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Microsoft.EntityFrameworkCore;
using Reservas.Application.Reservas.BuscarDisponibilidad;
using Reservas.Infrastructure.Cache;
using Reservas.Infrastructure.Persistencia;
using Reservas.Infrastructure.Proyeccion;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Story 3.2 (AC-E3.2.3) — caché de lectura contra Redis real: una búsqueda repetida se sirve desde caché
/// vigente (aunque el read-model haya cambiado por debajo), y la invalidación dirigida por ciudad refresca la
/// siguiente búsqueda. Se prueba la invalidación directa Y la disparada por un evento de catálogo vía el
/// <see cref="ProyectorCatalogo"/> real (extremo a extremo, sin sembrar estado a mano).
/// </summary>
[Collection("sqlserver")]
public sealed class CacheDisponibilidadTests(SqlServerFixture sql, RedisFixture redis)
{
    private static readonly JsonSerializerOptions _web = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly _entrada = new(2026, 11, 10);
    private static readonly DateOnly _salida = new(2026, 11, 12);

    private static string CiudadUnica() => "C" + Guid.NewGuid().ToString("N")[..12];

    private static EventoIntegracion Envelope(string tipo, int version, object data)
    {
        var e = new EventoIntegracion(Guid.CreateVersion7(), tipo, version,
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), null, data);
        return JsonSerializer.Deserialize<EventoIntegracion>(JsonSerializer.Serialize(e, _web), _web)!;
    }

    private async Task<Guid> SembrarHabitacionAsync(string ciudad, Guid hotelId)
    {
        var habId = Guid.CreateVersion7();
        await using var db = sql.CrearContexto();
        var fila = ProyeccionHabitacion.Nueva(habId);
        fila.AplicarAgregada(hotelId, ciudad, "Suite", "Piso 3", 4, 100m, 19m, "Habilitada", 1);
        db.ProyeccionesHabitacion.Add(fila);
        await db.SaveChangesAsync();
        return habId;
    }

    private BuscadorDisponibilidadCacheado Buscador(ReservasDbContext db, CacheDisponibilidadRedis cache) =>
        new(new BuscadorDisponibilidadSql(db), cache);

    [Fact]
    public async Task Repetida_sirve_de_cache_y_la_invalidacion_directa_refresca()
    {
        var ciudad = CiudadUnica();
        var habId = await SembrarHabitacionAsync(ciudad, Guid.CreateVersion7());
        var consulta = new BuscarDisponibilidadQuery(ciudad, _entrada, _salida, 2);
        var cache = new CacheDisponibilidadRedis(redis.Cache, new OpcionesCacheDisponibilidad());

        await using var db = sql.CrearContexto();
        var buscador = Buscador(db, cache);

        // 1) Miss → SQL → puebla caché.
        Assert.Equal(habId, Assert.Single(await buscador.BuscarAsync(consulta, CancellationToken.None)).HabitacionId);

        // Cambia el read-model por debajo SIN invalidar: deshabilita la habitación directamente en SQL.
        await using (var otro = sql.CrearContexto())
        {
            await otro.ProyeccionesHabitacion.Where(p => p.HabitacionId == habId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estado, "Deshabilitada"));
        }

        // 2) Repetida → HIT: sigue devolviendo la habitación (servida de caché, no del SQL ya cambiado).
        Assert.Equal(habId, Assert.Single(await buscador.BuscarAsync(consulta, CancellationToken.None)).HabitacionId);

        // 3) Invalidación dirigida por ciudad → la siguiente búsqueda refresca desde SQL (ya deshabilitada).
        await cache.InvalidarCiudadAsync(ciudad, CancellationToken.None);
        Assert.Empty(await buscador.BuscarAsync(consulta, CancellationToken.None));
    }

    // AC-E3.2.3 literal — la invalidación disparada por un EVENTO DE CATÁLOGO (vía ProyectorCatalogo) refresca.
    [Fact]
    public async Task Evento_de_catalogo_via_proyector_invalida_y_refresca()
    {
        var ciudad = CiudadUnica();
        var habId = await SembrarHabitacionAsync(ciudad, Guid.CreateVersion7());
        var consulta = new BuscarDisponibilidadQuery(ciudad, _entrada, _salida, 2);
        var cache = new CacheDisponibilidadRedis(redis.Cache, new OpcionesCacheDisponibilidad());

        await using var db = sql.CrearContexto();
        var buscador = Buscador(db, cache);

        // Puebla la caché.
        Assert.Single(await buscador.BuscarAsync(consulta, CancellationToken.None));

        // Un evento real de catálogo (deshabilitar la habitación) procesado por el proyector con el invalidador.
        await using (var dbProyector = sql.CrearContexto())
        {
            await new ProyectorCatalogo(dbProyector, cache).ProcesarAsync(
                Envelope(HabitacionDeshabilitadaV1.Tipo, 2, new HabitacionDeshabilitadaV1(habId, Guid.CreateVersion7())),
                CancellationToken.None);
        }

        // La siguiente búsqueda refresca desde el read-model ya actualizado → vacía.
        Assert.Empty(await buscador.BuscarAsync(consulta, CancellationToken.None));
    }
}
