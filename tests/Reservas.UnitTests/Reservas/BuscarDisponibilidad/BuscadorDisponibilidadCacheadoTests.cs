using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.BuscarDisponibilidad;
using Reservas.Infrastructure.Proyeccion;

namespace Reservas.UnitTests.BuscarDisponibilidad;

/// <summary>
/// Contrato de la decoradora de caché (AC-E3.2.3), aislado con fakes: en HIT sirve desde caché sin tocar el
/// read-model; en MISS consulta el read-model (el <c>calcular</c> que la caché invoca).
/// </summary>
public sealed class BuscadorDisponibilidadCacheadoTests
{
    private static readonly BuscarDisponibilidadQuery _consulta =
        new("Bogotá", new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 12), 2);

    private static HabitacionDisponibleDto Habitacion() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Bogotá", "Suite", "Piso 3", 4, 100m, 19m);

    [Fact]
    public async Task Hit_sirve_desde_cache_sin_consultar_el_read_model()
    {
        var cacheado = new[] { Habitacion() };
        var interno = new BuscadorFake(); // lanza si lo llaman vía el factory
        var cache = new CacheFake { Almacenado = cacheado };

        var decoradora = new BuscadorDisponibilidadCacheado(interno, cache);
        var resultado = await decoradora.BuscarAsync(_consulta, CancellationToken.None);

        Assert.Same(cacheado, resultado);
        Assert.False(interno.FueLlamado);
    }

    [Fact]
    public async Task Miss_consulta_el_read_model()
    {
        var delReadModel = new[] { Habitacion() };
        var interno = new BuscadorFake { Resultado = delReadModel };
        var cache = new CacheFake { Almacenado = null }; // miss → invoca el factory

        var decoradora = new BuscadorDisponibilidadCacheado(interno, cache);
        var resultado = await decoradora.BuscarAsync(_consulta, CancellationToken.None);

        Assert.Same(delReadModel, resultado);
        Assert.True(interno.FueLlamado);
    }

    private sealed class BuscadorFake : IBuscadorDisponibilidad
    {
        public IReadOnlyList<HabitacionDisponibleDto> Resultado { get; init; } = [];
        public bool FueLlamado { get; private set; }

        public Task<IReadOnlyList<HabitacionDisponibleDto>> BuscarAsync(BuscarDisponibilidadQuery consulta, CancellationToken ct)
        {
            FueLlamado = true;
            return Task.FromResult(Resultado);
        }
    }

    /// <summary>Caché fake: en hit devuelve lo almacenado sin invocar el factory; en miss ejecuta el factory.</summary>
    private sealed class CacheFake : ICacheDisponibilidad
    {
        public IReadOnlyList<HabitacionDisponibleDto>? Almacenado { get; init; }

        public async Task<IReadOnlyList<HabitacionDisponibleDto>> ObtenerOCalcularAsync(
            BuscarDisponibilidadQuery consulta,
            Func<CancellationToken, Task<IReadOnlyList<HabitacionDisponibleDto>>> calcular,
            CancellationToken ct) =>
            Almacenado ?? await calcular(ct);
    }
}
