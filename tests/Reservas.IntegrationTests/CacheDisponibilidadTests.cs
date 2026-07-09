using Microsoft.EntityFrameworkCore;
using Reservas.Application.Reservas.BuscarDisponibilidad;
using Reservas.Infrastructure.Cache;
using Reservas.Infrastructure.Persistencia;
using Reservas.Infrastructure.Proyeccion;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Story 3.2 (AC-E3.2.3) — caché de lectura contra Redis real: una búsqueda repetida se sirve desde caché
/// vigente (aunque el read-model haya cambiado por debajo), y una invalidación dirigida por ciudad hace que la
/// siguiente búsqueda refresque el resultado desde SQL.
/// </summary>
[Collection("sqlserver")]
public sealed class CacheDisponibilidadTests(SqlServerFixture sql, RedisFixture redis)
{
    private static readonly DateOnly _entrada = new(2026, 11, 10);
    private static readonly DateOnly _salida = new(2026, 11, 12);

    private static string CiudadUnica() => "C" + Guid.NewGuid().ToString("N")[..12];

    private async Task<Guid> SembrarHabitacionAsync(string ciudad)
    {
        var habId = Guid.CreateVersion7();
        await using var db = sql.CrearContexto();
        var fila = ProyeccionHabitacion.Nueva(habId);
        fila.AplicarAgregada(Guid.CreateVersion7(), ciudad, "Suite", "Piso 3", 4, 100m, 19m, "Habilitada", 1);
        db.ProyeccionesHabitacion.Add(fila);
        await db.SaveChangesAsync();
        return habId;
    }

    [Fact]
    public async Task Repetida_sirve_de_cache_y_la_invalidacion_por_ciudad_refresca()
    {
        var ciudad = CiudadUnica();
        var habId = await SembrarHabitacionAsync(ciudad);
        var consulta = new BuscarDisponibilidadQuery(ciudad, _entrada, _salida, 2);

        var cacheRedis = new CacheDisponibilidadRedis(redis.CrearCache(), new OpcionesCacheDisponibilidad());

        await using var db = sql.CrearContexto();
        var buscador = new BuscadorDisponibilidadCacheado(new BuscadorDisponibilidadSql(db), cacheRedis);

        // 1) Miss → SQL → puebla caché.
        var primera = await buscador.BuscarAsync(consulta, CancellationToken.None);
        Assert.Equal(habId, Assert.Single(primera).HabitacionId);

        // Cambia el read-model por debajo SIN invalidar: deshabilita la habitación directamente en SQL.
        await using (var otro = sql.CrearContexto())
        {
            await otro.ProyeccionesHabitacion
                .Where(p => p.HabitacionId == habId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estado, "Deshabilitada"));
        }

        // 2) Repetida → HIT: sigue devolviendo la habitación (servida de caché, no del SQL ya cambiado).
        var segunda = await buscador.BuscarAsync(consulta, CancellationToken.None);
        Assert.Equal(habId, Assert.Single(segunda).HabitacionId);

        // 3) Invalidación dirigida por ciudad → la siguiente búsqueda refresca desde SQL (ya deshabilitada).
        await cacheRedis.InvalidarCiudadAsync(ciudad, CancellationToken.None);
        var tercera = await buscador.BuscarAsync(consulta, CancellationToken.None);
        Assert.Empty(tercera);
    }
}
