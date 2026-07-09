using Microsoft.EntityFrameworkCore;
using Reservas.Domain.Reservas;
using Reservas.Infrastructure.Persistencia;
using Xunit;

namespace Reservas.IntegrationTests;

[Collection("sqlserver")]
public sealed class AntiOverbookingTests(SqlServerFixture fixture)
{
    private async Task ConfirmarAsync(Reserva reserva)
    {
        await using var db = fixture.CrearContexto();
        await new ReservaRepository(db).ConfirmarAsync(reserva, CancellationToken.None);
    }

    private async Task<int> ContarNochesAsync(Guid habitacionId)
    {
        await using var db = fixture.CrearContexto();
        return await db.NochesHabitacion.CountAsync(n => n.HabitacionId == habitacionId);
    }

    // AC-E1.5.0
    [Fact]
    public async Task Migracion_crea_el_schema_y_permite_confirmar()
    {
        var habitacion = Guid.NewGuid();
        var estancia = Estancia.Crear(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 12)); // 2 noches

        await ConfirmarAsync(Reserva.Crear(habitacion, estancia));

        Assert.Equal(2, await ContarNochesAsync(habitacion));
    }

    // AC-E1.5.1
    [Fact]
    public async Task Indice_unico_arbitra_dos_reservas_concurrentes_por_la_misma_noche()
    {
        var habitacion = Guid.NewGuid();
        var estancia = Estancia.Crear(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2)); // 1 noche

        var resultados = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try
            {
                await ConfirmarAsync(Reserva.Crear(habitacion, estancia));
                return "confirmada";
            }
            catch (HabitacionNoDisponibleException)
            {
                return "409";
            }
        }));

        Assert.Equal(1, resultados.Count(r => r == "confirmada"));
        Assert.Equal(1, resultados.Count(r => r == "409"));
        Assert.Equal(1, await ContarNochesAsync(habitacion)); // cero overbooking en el motor
    }

    // AC-E1.5.3 — falso 409: adyacentes no solapadas (check-out == check-in) ambas confirman
    [Fact]
    public async Task Estancias_adyacentes_no_solapadas_ambas_confirman()
    {
        var habitacion = Guid.NewGuid();
        var primera = Reserva.Crear(habitacion, Estancia.Crear(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 12)));  // noches 10, 11
        var segunda = Reserva.Crear(habitacion, Estancia.Crear(new DateOnly(2026, 9, 12), new DateOnly(2026, 9, 14)));  // noches 12, 13

        await ConfirmarAsync(primera);
        await ConfirmarAsync(segunda); // NO debe lanzar: la noche 12 no la ocupó la primera (rango semiabierto)

        Assert.Equal(4, await ContarNochesAsync(habitacion));
    }

    // AC-E1.5.3 — falso 409: habitaciones distintas, mismas fechas, ambas confirman
    [Fact]
    public async Task Habitaciones_distintas_con_las_mismas_fechas_ambas_confirman()
    {
        var estancia = Estancia.Crear(new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 22));
        var habitacionA = Guid.NewGuid();
        var habitacionB = Guid.NewGuid();

        await ConfirmarAsync(Reserva.Crear(habitacionA, estancia));
        await ConfirmarAsync(Reserva.Crear(habitacionB, estancia)); // el UNIQUE no cruza habitaciones

        Assert.Equal(2, await ContarNochesAsync(habitacionA));
        Assert.Equal(2, await ContarNochesAsync(habitacionB));
    }
}
