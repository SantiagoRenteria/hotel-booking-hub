using HotelBookingHub.Comun.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reservas.Application.Reservas.CrearReserva;
using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;
using Reservas.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Reservas.IntegrationTests;

/// <summary>
/// Money test G1 — el flujo crítico: N `CrearReservaCommand` concurrentes REALES sobre el mismo slot →
/// exactamente 1 confirmada (201) y N-1 rechazadas (409), 1 fila `Reserva` y 1 fila `OutboxMessages`.
/// Colección `G1` aislada (contenedor propio, `DisableParallelization`), reset por test. Reutiliza el
/// write-path de producción (mediator → Validation → Transaction → handler → EjecutorTransaccional → SQL).
/// </summary>
[Collection("G1")]
[Trait("Category", "G1")]
public sealed class MoneyTestG1Tests(SqlServerFixture fixture, ITestOutputHelper salida)
{
    private enum Resultado { Confirmada, Rechazada, Inesperado }

    // AC-E1.6c.2 — tabla exacta.
    [Theory]
    [InlineData(2)]
    [InlineData(50)]
    public Task N_concurrentes_producen_exactamente_una_confirmada(int n) => EjecutarMoneyTestAsync(n, semilla: null);

    // AC-E1.6c.3 — N sorteado 30–100 con semilla registrada para reproducibilidad.
    [Fact]
    public Task Money_test_fuzzeado_entre_30_y_100()
    {
        // Semilla parametrizable (AC-E1.6c.1): fijar MONEYTEST_SEED reproduce exactamente una corrida
        // fallida; si no está, se sortea por tiempo y se registra en la salida.
        var semilla = int.TryParse(Environment.GetEnvironmentVariable("MONEYTEST_SEED"), out var inyectada)
            ? inyectada
            : Environment.TickCount;
        var n = new Random(semilla).Next(30, 101);
        return EjecutarMoneyTestAsync(n, semilla);
    }

    private async Task EjecutarMoneyTestAsync(int n, int? semilla)
    {
        var habitacion = Guid.NewGuid();
        var comando = new ReservaTestDataBuilder().ParaHabitacion(habitacion).Construir();

        await using (var db = fixture.CrearContexto())
        {
            await HelpersOutbox.LimpiarAsync(db, CancellationToken.None);
        }

        await using var proveedor = ConstruirContenedorDeProduccion();

        var resultados = await Task.WhenAll(Enumerable.Range(0, n).Select(async _ =>
        {
            using var scope = proveedor.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            try
            {
                var r = await sender.Send(comando, CancellationToken.None);
                return r.EsExitoso ? Resultado.Confirmada : Resultado.Inesperado; // Invalido/NoEncontrado = defecto
            }
            catch (HabitacionNoDisponibleException)
            {
                return Resultado.Rechazada; // 2627/2601 o 1205 agotado → 409
            }
            catch (Exception)
            {
                return Resultado.Inesperado; // cualquier excepción NO mapeada = defecto (sería 500)
            }
        }));

        var confirmadas = resultados.Count(r => r == Resultado.Confirmada);
        var rechazadas = resultados.Count(r => r == Resultado.Rechazada);
        var inesperados = resultados.Count(r => r == Resultado.Inesperado);

        salida.WriteLine(
            $"Money test G1 · N={n} · semilla={(semilla?.ToString() ?? "n/a")} · " +
            $"confirmadas={confirmadas} rechazadas={rechazadas} inesperados={inesperados}");

        // Determinismo exacto (AC-E1.6c.2/.3): 1×201, N-1×409, 0 no mapeadas.
        Assert.Equal(0, inesperados);
        Assert.Equal(1, confirmadas);
        Assert.Equal(n - 1, rechazadas);

        await using var verify = fixture.CrearContexto();
        Assert.Equal(1, await verify.Reservas.CountAsync(r => r.HabitacionId == habitacion && r.Estado == EstadoReserva.Confirmada));
        Assert.Equal(1, await verify.OutboxMessages.CountAsync()); // 1 sola fila; 0 para las rechazadas
    }

    private ServiceProvider ConstruirContenedorDeProduccion()
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddSingleton<CalculadorPrecio>();
        // Orden idéntico a Program.cs: Logging+Validation (AddMediatorPipeline) y luego Transaction (infra).
        servicios.AddMediatorPipeline(typeof(CrearReservaCommand).Assembly);
        servicios.AddReservasInfrastructure($"{fixture.CadenaConexion};Max Pool Size=200;");
        return servicios.BuildServiceProvider();
    }
}

/// <summary>Colección G1 aislada (contenedor propio, sin paralelización) para el money test de concurrencia.</summary>
[CollectionDefinition("G1", DisableParallelization = true)]
public sealed class G1Collection : ICollectionFixture<SqlServerFixture>;
