using Reservas.Application.Reservas.BuscarDisponibilidad;
using Reservas.Domain.Reservas;
using Reservas.Infrastructure.Persistencia;
using Reservas.Infrastructure.Proyeccion;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Story 3.2 — el buscador real (<see cref="BuscadorDisponibilidadSql"/>) contra SQL Server: filtra por ciudad,
/// habitación activa, capacidad y noches libres del rango semiabierto <c>[entrada, salida)</c> (AC-E3.2.1), y
/// excluye lo no disponible (AC-E3.2.2). Cada test usa una ciudad única, de modo que no colisiona con otros.
/// </summary>
[Collection("sqlserver")]
public sealed class BusquedaDisponibilidadTests(SqlServerFixture fixture)
{
    private static readonly DateOnly _entrada = new(2026, 10, 10);
    private static readonly DateOnly _salida = new(2026, 10, 12); // noches 10, 11

    private static string CiudadUnica() => "C" + Guid.NewGuid().ToString("N")[..12];

    private async Task<Guid> SembrarHabitacionAsync(string ciudad, int capacidad, string estado)
    {
        var habId = Guid.CreateVersion7();
        await using var db = fixture.CrearContexto();
        var fila = ProyeccionHabitacion.Nueva(habId);
        fila.AplicarAgregada(Guid.CreateVersion7(), ciudad, "Suite", "Piso 3", capacidad, 100m, 19m, estado, 1);
        db.ProyeccionesHabitacion.Add(fila);
        await db.SaveChangesAsync();
        return habId;
    }

    private async Task OcuparAsync(Guid habitacionId, DateOnly entrada, DateOnly salida)
    {
        await using var db = fixture.CrearContexto();
        var ejecutor = new EjecutorTransaccional(db);
        await ejecutor.EjecutarAsync(
            _ =>
            {
                new ReservaRepository(db).Agregar(Reserva.Crear(habitacionId, Estancia.Crear(entrada, salida)));
                return Task.CompletedTask;
            },
            CancellationToken.None);
    }

    private async Task<IReadOnlyList<HabitacionDisponibleDto>> BuscarAsync(string ciudad, int huespedes = 2)
    {
        await using var db = fixture.CrearContexto();
        return await new BuscadorDisponibilidadSql(db)
            .BuscarAsync(new BuscarDisponibilidadQuery(ciudad, _entrada, _salida, huespedes), CancellationToken.None);
    }

    // AC-E3.2.1 — activa, con capacidad y todas las noches libres: aparece con sus datos de catálogo.
    [Fact]
    public async Task Activa_con_capacidad_y_noches_libres_aparece()
    {
        var ciudad = CiudadUnica();
        var habId = await SembrarHabitacionAsync(ciudad, capacidad: 4, estado: "Habilitada");

        var resultado = await BuscarAsync(ciudad, huespedes: 2);

        var dto = Assert.Single(resultado);
        Assert.Equal(habId, dto.HabitacionId);
        Assert.Equal(ciudad, dto.Ciudad);
        Assert.Equal(4, dto.Capacidad);
        Assert.Equal("Suite", dto.Tipo);
        Assert.Equal(100m, dto.CostoBase);
        Assert.Equal(19m, dto.Impuestos);
    }

    // AC-E3.2.2 (tabla) — atributos de la propia habitación que la descartan: capacidad insuficiente o
    // deshabilitada individualmente. El caso "hotel deshabilitado" es una dimensión aparte y se cubre por su
    // flujo real de eventos en HotelDeshabilitadoBusquedaTests (no se siembra aquí).
    [Theory]
    [InlineData(1, "Habilitada")]    // capacidad < huéspedes(2)
    [InlineData(4, "Deshabilitada")] // habitación deshabilitada por sí misma
    public async Task Habitacion_que_no_cumple_atributos_no_aparece(int capacidad, string estado)
    {
        var ciudad = CiudadUnica();
        await SembrarHabitacionAsync(ciudad, capacidad, estado);

        var resultado = await BuscarAsync(ciudad, huespedes: 2);

        Assert.Empty(resultado);
    }

    // AC-E3.2.2 — una habitación de otra ciudad no aparece en la búsqueda de esta ciudad.
    [Fact]
    public async Task Habitacion_de_otra_ciudad_no_aparece()
    {
        var ciudadBuscada = CiudadUnica();
        await SembrarHabitacionAsync(CiudadUnica(), capacidad: 4, estado: "Habilitada"); // otra ciudad

        var resultado = await BuscarAsync(ciudadBuscada);

        Assert.Empty(resultado);
    }

    // AC-E3.2.2 — fila parcial (un delta llegó antes del alta): no hidratada → no aparece.
    [Fact]
    public async Task Fila_no_hidratada_no_aparece()
    {
        var ciudad = CiudadUnica();
        var habId = Guid.CreateVersion7();
        await using (var db = fixture.CrearContexto())
        {
            db.ProyeccionesHabitacion.Add(ProyeccionHabitacion.Nueva(habId)); // sin AplicarAgregada → parcial
            await db.SaveChangesAsync();
        }

        var resultado = await BuscarAsync(ciudad);

        Assert.Empty(resultado);
    }

    // AC-E3.2.2 — reservada en el rango exacto: no aparece.
    [Fact]
    public async Task Reservada_en_el_rango_no_aparece()
    {
        var ciudad = CiudadUnica();
        var habId = await SembrarHabitacionAsync(ciudad, capacidad: 4, estado: "Habilitada");
        await OcuparAsync(habId, _entrada, _salida); // ocupa noches 10 y 11

        var resultado = await BuscarAsync(ciudad);

        Assert.Empty(resultado);
    }

    // AC-E3.2.2 — solapamiento parcial (comparte al menos una noche): no aparece.
    [Fact]
    public async Task Solapamiento_parcial_no_aparece()
    {
        var ciudad = CiudadUnica();
        var habId = await SembrarHabitacionAsync(ciudad, capacidad: 4, estado: "Habilitada");
        // Ocupa [11, 13) → noches 11, 12. La búsqueda [10, 12) comparte la noche 11.
        await OcuparAsync(habId, new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 13));

        var resultado = await BuscarAsync(ciudad);

        Assert.Empty(resultado);
    }

    // AC-E3.2.1 (borde) — reserva adyacente por semiabierto (su salida == entrada de la búsqueda): SÍ aparece.
    [Fact]
    public async Task Adyacente_por_semiabierto_si_aparece()
    {
        var ciudad = CiudadUnica();
        var habId = await SembrarHabitacionAsync(ciudad, capacidad: 4, estado: "Habilitada");
        // Ocupa [8, 10) → noches 8, 9. La búsqueda [10, 12) no comparte ninguna noche (la 10 quedó libre).
        await OcuparAsync(habId, new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 10));

        var resultado = await BuscarAsync(ciudad);

        Assert.Equal(habId, Assert.Single(resultado).HabitacionId);
    }
}
