using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.CancelarEnUnPaso;
using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;
using Reservas.UnitTests.CrearReserva;      // ColaOutboxFake, RepositorioReservasFake
using Reservas.UnitTests.SolicitarCancelacion; // RelojFijo
using Xunit;

namespace Reservas.UnitTests.CancelarEnUnPaso;

/// <summary>
/// Story 4.3 (Task 1) — handler del atajo con fakes. Verifica que compone solicitar + resolver y emite AMBOS
/// eventos (auditoría completa): aprobar → Cancelada + {Registrada, ReservaCancelada} + slots liberados;
/// rechazar → Confirmada + {Registrada, Rechazada} y NUNCA ReservaCancelada; aislamiento por agente (403).
/// </summary>
public sealed class CancelarEnUnPasoCommandHandlerTests
{
    private const string Dueno = "agente@hotel.com";
    private static readonly DateOnly _entrada = new(2026, 9, 1);
    private readonly RepositorioReservasFake _repo = new();
    private readonly ColaOutboxFake _outbox = new();

    private sealed class ContextoFake : IContextoAgente
    {
        public string? AgenteActual { get; init; }
    }

    private CancelarEnUnPasoCommandHandler Handler(string? agente) =>
        new(_repo, new ContextoFake { AgenteActual = agente }, new CalculadorPenalidad(), _outbox, RelojFijo.EnFecha(_entrada.AddDays(-10)));

    private Reserva SembrarConfirmada(string? dueno = Dueno)
    {
        var reserva = Reserva.Crear(Guid.NewGuid(), Estancia.Crear(_entrada, _entrada.AddDays(2)), agenteEmail: dueno, precioTotal: 500m);
        _repo.Existentes.Add(reserva);
        return reserva;
    }

    private Reserva SembrarConEmails(string huespedEmail, string agenteEmail)
    {
        var huesped = Huesped.Crear(
            "Andrés", "Pérez", new DateOnly(1990, 5, 1), "M",
            Documento.Crear("CC", "1234567"), huespedEmail, "3001234567");
        var reserva = Reserva.Crear(
            Guid.NewGuid(), Estancia.Crear(_entrada, _entrada.AddDays(2)),
            huespedes: [huesped], agenteEmail: agenteEmail, precioTotal: 500m);
        _repo.Existentes.Add(reserva);
        return reserva;
    }

    private static CancelarEnUnPasoCommand Comando(Guid id, DecisionCancelacion decision, string? motivoRechazo = null) =>
        new(id, "CambioDePlanes", "detalle", IniciadorCancelacion.Agente, decision, motivoRechazo);

    [Fact]
    public async Task Atajo_aprobando_cancela_libera_slots_y_emite_ambos_eventos()
    {
        var reserva = SembrarConfirmada();

        var resultado = await Handler(Dueno).Handle(Comando(reserva.Id, DecisionCancelacion.AprobarAplicandoPenalidad), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal("Cancelada", resultado.Valor!.Estado);
        Assert.Empty(reserva.Noches);

        Assert.Equal(2, _outbox.Encolados.Count);
        Assert.Equal(SolicitudCancelacionRegistradaV1.Tipo, _outbox.Encolados[0].Tipo);
        Assert.Equal(ReservaCanceladaV1.Tipo, _outbox.Encolados[1].Tipo);
        Assert.All(_outbox.Encolados, e => Assert.Equal(reserva.Id, e.AggregateId)); // misma order key
    }

    [Fact]
    public async Task Atajo_rechazando_vuelve_a_confirmada_y_nunca_emite_ReservaCancelada()
    {
        var reserva = SembrarConfirmada();

        var resultado = await Handler(Dueno).Handle(Comando(reserva.Id, DecisionCancelacion.Rechazar, "No procede."), CancellationToken.None);

        Assert.Equal("Confirmada", resultado.Valor!.Estado);
        Assert.Equal(2, reserva.Noches.Count); // no toca slots
        Assert.Equal(2, _outbox.Encolados.Count);
        Assert.Equal(SolicitudCancelacionRegistradaV1.Tipo, _outbox.Encolados[0].Tipo);
        Assert.Equal(SolicitudCancelacionRechazadaV1.Tipo, _outbox.Encolados[1].Tipo);
        Assert.DoesNotContain(_outbox.Encolados, e => e.Tipo == ReservaCanceladaV1.Tipo);
    }

    [Fact]
    public async Task El_evento_de_solicitud_del_atajo_incluye_los_emails_del_destinatario()
    {
        // Story 5.2 (party-mode opción a): el atajo también puebla HuespedEmail+AgenteEmail en el evento de solicitud.
        var reserva = SembrarConEmails(huespedEmail: "andres@example.com", agenteEmail: Dueno);

        await Handler(Dueno).Handle(Comando(reserva.Id, DecisionCancelacion.AprobarAplicandoPenalidad), CancellationToken.None);

        var data = Assert.IsType<SolicitudCancelacionRegistradaV1>(_outbox.Encolados[0].Data);
        Assert.Equal("andres@example.com", data.HuespedEmail);
        Assert.Equal(Dueno, data.AgenteEmail);
    }

    [Fact]
    public async Task El_evento_de_resolucion_del_atajo_aprobando_incluye_el_email_del_huesped()
    {
        // Story 5.3: el atajo también puebla HuespedEmail en el evento de resolución (ReservaCancelada).
        var reserva = SembrarConEmails(huespedEmail: "andres@example.com", agenteEmail: Dueno);

        await Handler(Dueno).Handle(Comando(reserva.Id, DecisionCancelacion.AprobarAplicandoPenalidad), CancellationToken.None);

        var data = Assert.IsType<ReservaCanceladaV1>(_outbox.Encolados[1].Data); // [0]=Registrada, [1]=resolución
        Assert.Equal("andres@example.com", data.HuespedEmail);
    }

    [Fact]
    public async Task El_evento_de_resolucion_del_atajo_rechazando_incluye_el_email_del_huesped()
    {
        var reserva = SembrarConEmails(huespedEmail: "andres@example.com", agenteEmail: Dueno);

        await Handler(Dueno).Handle(Comando(reserva.Id, DecisionCancelacion.Rechazar, "No procede."), CancellationToken.None);

        var data = Assert.IsType<SolicitudCancelacionRechazadaV1>(_outbox.Encolados[1].Data);
        Assert.Equal("andres@example.com", data.HuespedEmail);
    }

    [Fact]
    public async Task Sin_identidad_devuelve_prohibido_sin_encolar()
    {
        var reserva = SembrarConfirmada();
        var resultado = await Handler(agente: null).Handle(Comando(reserva.Id, DecisionCancelacion.AprobarAplicandoPenalidad), CancellationToken.None);

        Assert.Equal(EstadoResultado.Prohibido, resultado.Estado);
        Assert.Empty(_outbox.Encolados);
    }

    [Fact]
    public async Task Agente_ajeno_devuelve_no_encontrado_sin_mutar()
    {
        // Story 6.3: reserva ajena → 404 (no 403; no filtra existencia entre agentes).
        var reserva = SembrarConfirmada(dueno: Dueno);
        var resultado = await Handler("otro@agencia.com").Handle(Comando(reserva.Id, DecisionCancelacion.AprobarAplicandoPenalidad), CancellationToken.None);

        Assert.Equal(EstadoResultado.NoEncontrado, resultado.Estado);
        Assert.Equal(EstadoReserva.Confirmada, reserva.Estado);
        Assert.Empty(_outbox.Encolados);
    }

    [Fact]
    public async Task Reserva_inexistente_devuelve_no_encontrado()
    {
        var resultado = await Handler(Dueno).Handle(Comando(Guid.NewGuid(), DecisionCancelacion.AprobarAplicandoPenalidad), CancellationToken.None);
        Assert.Equal(EstadoResultado.NoEncontrado, resultado.Estado);
        Assert.Empty(_outbox.Encolados);
    }
}
