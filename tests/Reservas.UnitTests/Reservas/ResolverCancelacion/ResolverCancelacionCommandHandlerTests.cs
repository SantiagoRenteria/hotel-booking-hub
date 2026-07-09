using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.ResolverCancelacion;
using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;
using Reservas.UnitTests.CrearReserva; // ColaOutboxFake, RepositorioReservasFake
using Reservas.UnitTests.SolicitarCancelacion; // RelojFijo
using Xunit;

namespace Reservas.UnitTests.ResolverCancelacion;

/// <summary>
/// Story 4.2 (Task 3) — handler de resolución con fakes. Verifica aprobar (evento <c>ReservaCancelada</c> +
/// penalidad aplicada + slots liberados), condonar (override), rechazar (evento <c>SolicitudCancelacionRechazada</c>),
/// el aislamiento por agente (ajeno / sin identidad → 403 sin encolar), 404, y la propagación del guard de estado.
/// </summary>
public sealed class ResolverCancelacionCommandHandlerTests
{
    private const string Dueno = "agente@hotel.com";
    private static readonly DateOnly _entrada = new(2026, 9, 1);
    private readonly RepositorioReservasFake _repo = new();
    private readonly ColaOutboxFake _outbox = new();

    private sealed class ContextoFake : IContextoAgente
    {
        public string? AgenteActual { get; init; }
    }

    private ResolverCancelacionCommandHandler Handler(string? agente) =>
        new(_repo, new ContextoFake { AgenteActual = agente }, _outbox, RelojFijo.EnFecha(_entrada.AddDays(-5)));

    private Reserva SembrarSolicitada(string? dueno = Dueno, int diasAntelacion = 10)
    {
        var reserva = Reserva.Crear(Guid.NewGuid(), Estancia.Crear(_entrada, _entrada.AddDays(2)), agenteEmail: dueno, precioTotal: 500m);
        reserva.SolicitarCancelacion(
            MotivoCancelacion.Crear("CambioDePlanes", "detalle"),
            IniciadorCancelacion.Viajero,
            _entrada.AddDays(-diasAntelacion),
            new CalculadorPenalidad());
        _repo.Existentes.Add(reserva);
        return reserva;
    }

    private Reserva SembrarSolicitadaConHuesped(string huespedEmail)
    {
        var huesped = Huesped.Crear(
            "Andrés", "Pérez", new DateOnly(1990, 5, 1), "M",
            Documento.Crear("CC", "1234567"), huespedEmail, "3001234567");
        var reserva = Reserva.Crear(
            Guid.NewGuid(), Estancia.Crear(_entrada, _entrada.AddDays(2)),
            huespedes: [huesped], agenteEmail: Dueno, precioTotal: 500m);
        reserva.SolicitarCancelacion(
            MotivoCancelacion.Crear("CambioDePlanes", "detalle"),
            IniciadorCancelacion.Viajero, _entrada.AddDays(-10), new CalculadorPenalidad());
        _repo.Existentes.Add(reserva);
        return reserva;
    }

    [Fact]
    public async Task Aprobar_incluye_el_email_del_huesped_en_el_evento()
    {
        // Story 5.3 (party-mode opción a): el emisor puebla HuespedEmail en el evento de resolución (viajero).
        var reserva = SembrarSolicitadaConHuesped("andres@example.com");
        var cmd = new ResolverCancelacionCommand(reserva.Id, DecisionCancelacion.AprobarAplicandoPenalidad, null);

        await Handler(Dueno).Handle(cmd, CancellationToken.None);

        var data = Assert.IsType<ReservaCanceladaV1>(Assert.Single(_outbox.Encolados).Data);
        Assert.Equal("andres@example.com", data.HuespedEmail);
    }

    [Fact]
    public async Task Rechazar_incluye_el_email_del_huesped_en_el_evento()
    {
        var reserva = SembrarSolicitadaConHuesped("andres@example.com");
        var cmd = new ResolverCancelacionCommand(reserva.Id, DecisionCancelacion.Rechazar, "No procede.");

        await Handler(Dueno).Handle(cmd, CancellationToken.None);

        var data = Assert.IsType<SolicitudCancelacionRechazadaV1>(Assert.Single(_outbox.Encolados).Data);
        Assert.Equal("andres@example.com", data.HuespedEmail);
    }

    [Fact]
    public async Task Aprobar_aplicando_cancela_libera_slots_y_encola_ReservaCancelada()
    {
        var reserva = SembrarSolicitada(diasAntelacion: 10); // penalidad congelada 100%
        var cmd = new ResolverCancelacionCommand(reserva.Id, DecisionCancelacion.AprobarAplicandoPenalidad, null);

        var resultado = await Handler(Dueno).Handle(cmd, CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal("Cancelada", resultado.Valor!.Estado);
        Assert.Equal(100m, resultado.Valor.PenalidadAplicadaPorcentaje);
        Assert.Empty(reserva.Noches);

        var encolado = Assert.Single(_outbox.Encolados);
        Assert.Equal(ReservaCanceladaV1.Tipo, encolado.Tipo);
        Assert.Equal(reserva.Id, encolado.AggregateId);
        var data = Assert.IsType<ReservaCanceladaV1>(encolado.Data);
        Assert.Equal(100m, data.PenalidadAplicadaPorcentaje);
        Assert.False(data.PenalidadFueOverride);
    }

    [Fact]
    public async Task Aprobar_condonando_aplica_cero_con_override()
    {
        var reserva = SembrarSolicitada(diasAntelacion: 10); // sugerida 100%
        var cmd = new ResolverCancelacionCommand(reserva.Id, DecisionCancelacion.AprobarCondonandoPenalidad, null);

        var resultado = await Handler(Dueno).Handle(cmd, CancellationToken.None);

        Assert.Equal(0m, resultado.Valor!.PenalidadAplicadaPorcentaje);
        var data = Assert.IsType<ReservaCanceladaV1>(Assert.Single(_outbox.Encolados).Data);
        Assert.True(data.PenalidadFueOverride);
    }

    [Fact]
    public async Task Rechazar_vuelve_a_confirmada_y_encola_rechazo()
    {
        var reserva = SembrarSolicitada();
        var cmd = new ResolverCancelacionCommand(reserva.Id, DecisionCancelacion.Rechazar, "No procede.");

        var resultado = await Handler(Dueno).Handle(cmd, CancellationToken.None);

        Assert.Equal("Confirmada", resultado.Valor!.Estado);
        Assert.Equal(2, reserva.Noches.Count); // no toca slots
        var data = Assert.IsType<SolicitudCancelacionRechazadaV1>(Assert.Single(_outbox.Encolados).Data);
        Assert.Equal("No procede.", data.MotivoRechazo);
    }

    [Fact]
    public async Task Agente_ajeno_devuelve_prohibido_sin_encolar()
    {
        var reserva = SembrarSolicitada(dueno: Dueno);
        var cmd = new ResolverCancelacionCommand(reserva.Id, DecisionCancelacion.AprobarAplicandoPenalidad, null);

        var resultado = await Handler("otro@agencia.com").Handle(cmd, CancellationToken.None);

        Assert.Equal(EstadoResultado.Prohibido, resultado.Estado);
        Assert.Empty(_outbox.Encolados);
        Assert.Equal(EstadoReserva.CancelacionSolicitada, reserva.Estado); // intacta
    }

    [Fact]
    public async Task Sin_identidad_de_agente_devuelve_prohibido()
    {
        var reserva = SembrarSolicitada();
        var cmd = new ResolverCancelacionCommand(reserva.Id, DecisionCancelacion.Rechazar, "x");

        var resultado = await Handler(agente: null).Handle(cmd, CancellationToken.None);

        Assert.Equal(EstadoResultado.Prohibido, resultado.Estado);
        Assert.Empty(_outbox.Encolados);
    }

    [Fact]
    public async Task Reserva_inexistente_devuelve_no_encontrado()
    {
        var cmd = new ResolverCancelacionCommand(Guid.NewGuid(), DecisionCancelacion.Rechazar, "x");

        var resultado = await Handler(Dueno).Handle(cmd, CancellationToken.None);

        Assert.Equal(EstadoResultado.NoEncontrado, resultado.Estado);
        Assert.Empty(_outbox.Encolados);
    }

    [Fact]
    public async Task Reserva_no_solicitada_propaga_transicion_invalida()
    {
        // Confirmada (sin solicitud) pertenece al agente pero no es resoluble → guard de dominio.
        var reserva = Reserva.Crear(Guid.NewGuid(), Estancia.Crear(_entrada, _entrada.AddDays(2)), agenteEmail: Dueno);
        _repo.Existentes.Add(reserva);
        var cmd = new ResolverCancelacionCommand(reserva.Id, DecisionCancelacion.Rechazar, "x");

        await Assert.ThrowsAsync<TransicionEstadoInvalidaException>(() => Handler(Dueno).Handle(cmd, CancellationToken.None));
        Assert.Empty(_outbox.Encolados);
    }
}
