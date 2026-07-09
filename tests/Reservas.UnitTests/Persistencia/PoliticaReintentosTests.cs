using Reservas.Infrastructure.Persistencia;
using Xunit;

namespace Reservas.UnitTests.Persistencia;

public sealed class PoliticaReintentosTests
{
    private sealed class FalloTransitorio : Exception;

    private static bool EsTransitorio(Exception ex) => ex is FalloTransitorio;

    [Fact]
    public async Task Reintenta_y_termina_en_exito_tras_fallos_transitorios()
    {
        var intentos = 0;

        await PoliticaReintentos.EjecutarAsync(
            _ =>
            {
                intentos++;
                if (intentos < 3)
                {
                    throw new FalloTransitorio();
                }
                return Task.CompletedTask;
            },
            CancellationToken.None,
            maxIntentos: 3,
            esReintentable: EsTransitorio);

        Assert.Equal(3, intentos);
    }

    [Fact]
    public async Task Se_rinde_tras_agotar_los_intentos()
    {
        var intentos = 0;

        await Assert.ThrowsAsync<FalloTransitorio>(() => PoliticaReintentos.EjecutarAsync(
            _ =>
            {
                intentos++;
                throw new FalloTransitorio();
            },
            CancellationToken.None,
            maxIntentos: 3,
            esReintentable: EsTransitorio));

        Assert.Equal(3, intentos);
    }

    [Fact]
    public async Task No_reintenta_una_excepcion_no_reintentable()
    {
        var intentos = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => PoliticaReintentos.EjecutarAsync(
            _ =>
            {
                intentos++;
                throw new InvalidOperationException();
            },
            CancellationToken.None,
            maxIntentos: 3,
            esReintentable: EsTransitorio));

        Assert.Equal(1, intentos); // se lanza al primer intento, sin reintentos
    }
}
