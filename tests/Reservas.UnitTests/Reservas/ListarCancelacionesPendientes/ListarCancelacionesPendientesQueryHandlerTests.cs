using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.ListarCancelacionesPendientes;
using Reservas.UnitTests.SolicitarCancelacion; // RelojFijo
using Xunit;

namespace Reservas.UnitTests.ListarCancelacionesPendientes;

/// <summary>
/// Story 4.3 (Task 3, AC-E4.3.3) — el handler deriva "días en espera" con el reloj inyectable, aísla por agente
/// (fail-closed → 403) y NO expira nada (solo expone antigüedad).
/// </summary>
public sealed class ListarCancelacionesPendientesQueryHandlerTests
{
    private static readonly DateOnly _hoy = new(2026, 9, 20);

    private sealed class ContextoFake : IContextoAgente
    {
        public string? AgenteActual { get; init; }
    }

    private sealed class LectorFake : ILectorCancelacionesPendientes
    {
        public IReadOnlyList<CancelacionPendienteFila> Filas { get; init; } = [];
        public string? AgenteRecibido { get; private set; }

        public Task<IReadOnlyList<CancelacionPendienteFila>> ListarPendientesAsync(string agenteEmail, CancellationToken ct)
        {
            AgenteRecibido = agenteEmail;
            return Task.FromResult(Filas);
        }
    }

    [Fact]
    public async Task Deriva_dias_en_espera_y_aisla_por_agente()
    {
        var lector = new LectorFake
        {
            Filas = [new CancelacionPendienteFila(Guid.NewGuid(), _hoy.AddDays(-5), "CambioDePlanes", 100m)],
        };
        var handler = new ListarCancelacionesPendientesQueryHandler(
            lector, new ContextoFake { AgenteActual = "Agente@Hotel.com" }, RelojFijo.EnFecha(_hoy));

        var resultado = await handler.Handle(new ListarCancelacionesPendientesQuery(), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        var item = Assert.Single(resultado.Valor!);
        Assert.Equal(5, item.DiasEnEspera); // hoy - (hoy-5)
        Assert.Equal(100m, item.PenalidadSugeridaPorcentaje);
        Assert.Equal("agente@hotel.com", lector.AgenteRecibido); // canónico (minúsculas)
    }

    [Fact]
    public async Task Sin_identidad_devuelve_prohibido()
    {
        var handler = new ListarCancelacionesPendientesQueryHandler(
            new LectorFake(), new ContextoFake { AgenteActual = null }, RelojFijo.EnFecha(_hoy));

        var resultado = await handler.Handle(new ListarCancelacionesPendientesQuery(), CancellationToken.None);

        Assert.Equal(EstadoResultado.Prohibido, resultado.Estado);
    }

    [Fact]
    public async Task Sin_pendientes_devuelve_lista_vacia()
    {
        var handler = new ListarCancelacionesPendientesQueryHandler(
            new LectorFake(), new ContextoFake { AgenteActual = "a@b.com" }, RelojFijo.EnFecha(_hoy));

        var resultado = await handler.Handle(new ListarCancelacionesPendientesQuery(), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Empty(resultado.Valor!);
    }
}
