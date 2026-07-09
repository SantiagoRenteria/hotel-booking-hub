using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.ListarReservasDelAgente;
using Reservas.Application.Reservas.ObtenerReservaDetalle;

namespace Reservas.UnitTests.ListarReservas;

/// <summary>
/// Story 3.3 — contrato de los handlers de lectura, aislado con fakes: identidad server-side (fail-closed → 403),
/// aislamiento delegado al lector, y detalle ajeno/inexistente → 404.
/// </summary>
public sealed class ListadoReservasHandlersTests
{
    private static ReservaListadoDto Item() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Bogotá", "Suite", "Piso 3",
            new DateOnly(2027, 1, 10), new DateOnly(2027, 1, 12), "Confirmada", 500m);

    // Listado: sin identidad → 403 y NO consulta el lector (fail-closed).
    [Fact]
    public async Task Listar_sin_identidad_es_prohibido_y_no_consulta()
    {
        var lector = new LectorFake();
        var handler = new ListarReservasDelAgenteQueryHandler(lector, new ContextoFake { AgenteActual = null });

        var resultado = await handler.Handle(new ListarReservasDelAgenteQuery(), CancellationToken.None);

        Assert.Equal(EstadoResultado.Prohibido, resultado.Estado);
        Assert.Null(lector.AgenteRecibido);
    }

    // Listado: con identidad → 200 y filtra por ese agente.
    [Fact]
    public async Task Listar_con_identidad_devuelve_ok_filtrando_por_agente()
    {
        var lector = new LectorFake { Listado = [Item()] };
        var handler = new ListarReservasDelAgenteQueryHandler(lector, new ContextoFake { AgenteActual = "a@test.com" });

        var resultado = await handler.Handle(new ListarReservasDelAgenteQuery(), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Single(resultado.Valor!);
        Assert.Equal("a@test.com", lector.AgenteRecibido);
    }

    [Fact]
    public async Task Detalle_sin_identidad_es_prohibido()
    {
        var handler = new ObtenerReservaDetalleQueryHandler(new LectorFake(), new ContextoFake { AgenteActual = "" });

        var resultado = await handler.Handle(new ObtenerReservaDetalleQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(EstadoResultado.Prohibido, resultado.Estado);
    }

    // Detalle ajeno/inexistente (lector devuelve null) → 404, sin filtrar contenido.
    [Fact]
    public async Task Detalle_inexistente_o_ajeno_es_no_encontrado()
    {
        var handler = new ObtenerReservaDetalleQueryHandler(
            new LectorFake { Detalle = null }, new ContextoFake { AgenteActual = "a@test.com" });

        var resultado = await handler.Handle(new ObtenerReservaDetalleQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(EstadoResultado.NoEncontrado, resultado.Estado);
    }

    [Fact]
    public async Task Detalle_propio_devuelve_ok()
    {
        var detalle = new ReservaDetalleDto(Item(), [], null);
        var handler = new ObtenerReservaDetalleQueryHandler(
            new LectorFake { Detalle = detalle }, new ContextoFake { AgenteActual = "a@test.com" });

        var resultado = await handler.Handle(new ObtenerReservaDetalleQuery(detalle.Reserva.Id), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal(detalle.Reserva.Id, resultado.Valor!.Reserva.Id);
    }

    private sealed class ContextoFake : IContextoAgente
    {
        public string? AgenteActual { get; init; }
    }

    private sealed class LectorFake : ILectorReservasAgente
    {
        public IReadOnlyList<ReservaListadoDto> Listado { get; init; } = [];
        public ReservaDetalleDto? Detalle { get; init; }
        public string? AgenteRecibido { get; private set; }

        public Task<IReadOnlyList<ReservaListadoDto>> ListarAsync(string agenteEmail, CancellationToken ct)
        {
            AgenteRecibido = agenteEmail;
            return Task.FromResult(Listado);
        }

        public Task<ReservaDetalleDto?> ObtenerDetalleAsync(Guid reservaId, string agenteEmail, CancellationToken ct)
        {
            AgenteRecibido = agenteEmail;
            return Task.FromResult(Detalle);
        }
    }
}
