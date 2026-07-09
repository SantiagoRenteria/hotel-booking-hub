using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Reservas.SolicitarCancelacion;
using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;
using Reservas.UnitTests.CrearReserva; // ColaOutboxFake, RepositorioReservasFake
using Xunit;

namespace Reservas.UnitTests.SolicitarCancelacion;

/// <summary>
/// Story 4.1 (Task 3) — handler de la solicitud de cancelación con fakes (sin BD ni outbox real). Verifica el
/// happy path (transición + penalidad congelada por el reloj inyectado + evento encolado), el 404, y que los
/// guards de dominio (duplicada / estancia iniciada → 409) se PROPAGAN sin encolar evento.
/// </summary>
public sealed class SolicitarCancelacionCommandHandlerTests
{
    private readonly RepositorioReservasFake _repo = new();
    private readonly ColaOutboxFake _outbox = new();
    private static readonly DateOnly _entrada = new(2026, 9, 1);

    private SolicitarCancelacionCommandHandler CrearHandler(DateOnly hoy) =>
        new(_repo, new CalculadorPenalidad(), _outbox, RelojFijo.EnFecha(hoy));

    private Reserva SembrarConfirmada(decimal precio = 500m)
    {
        var reserva = Reserva.Crear(Guid.NewGuid(), Estancia.Crear(_entrada, _entrada.AddDays(2)), precioTotal: precio);
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

    private static SolicitarCancelacionCommand Comando(Guid reservaId) =>
        new(reservaId, "CambioDePlanes", "El viajero ya no puede asistir.", IniciadorCancelacion.Viajero);

    [Fact]
    public async Task Happy_path_transiciona_congela_penalidad_y_encola_evento()
    {
        var reserva = SembrarConfirmada(precio: 500m);
        var hoy = _entrada.AddDays(-40); // >30 días → 0%

        var resultado = await CrearHandler(hoy).Handle(Comando(reserva.Id), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal("CancelacionSolicitada", resultado.Valor!.Estado);
        Assert.Equal(0m, resultado.Valor.PenalidadPorcentaje);
        Assert.Equal(0m, resultado.Valor.PenalidadMonto);
        Assert.Equal(EstadoReserva.CancelacionSolicitada, reserva.Estado);

        var encolado = Assert.Single(_outbox.Encolados);
        Assert.Equal(SolicitudCancelacionRegistradaV1.Tipo, encolado.Tipo);
        Assert.Equal(reserva.Id, encolado.AggregateId);
        var data = Assert.IsType<SolicitudCancelacionRegistradaV1>(encolado.Data);
        Assert.Equal(reserva.Id, data.AggregateId);
        Assert.Equal("Viajero", data.Iniciador);
        Assert.Equal(0m, data.PenalidadPorcentaje);
        Assert.Equal(hoy, data.FechaSolicitud); // congelada con el reloj inyectado
    }

    [Fact]
    public async Task Evento_incluye_emails_del_huesped_principal_y_del_agente()
    {
        // Story 5.2 (party-mode opción a): el emisor puebla el evento con los destinatarios desde la reserva.
        var reserva = SembrarConEmails(huespedEmail: "andres@example.com", agenteEmail: "carolina@agencia.com");

        await CrearHandler(_entrada.AddDays(-40)).Handle(Comando(reserva.Id), CancellationToken.None);

        var data = Assert.IsType<SolicitudCancelacionRegistradaV1>(Assert.Single(_outbox.Encolados).Data);
        Assert.Equal("andres@example.com", data.HuespedEmail);
        Assert.Equal("carolina@agencia.com", data.AgenteEmail);
    }

    [Fact]
    public async Task Penalidad_del_cien_por_ciento_incluye_monto_informativo_sobre_el_precio()
    {
        var reserva = SembrarConfirmada(precio: 500m);
        var hoy = _entrada.AddDays(-10); // <30 días → 100%

        var resultado = await CrearHandler(hoy).Handle(Comando(reserva.Id), CancellationToken.None);

        Assert.Equal(100m, resultado.Valor!.PenalidadPorcentaje);
        Assert.Equal(500m, resultado.Valor.PenalidadMonto); // 100% de 500
    }

    [Fact]
    public async Task Reserva_inexistente_devuelve_no_encontrado_sin_encolar()
    {
        var resultado = await CrearHandler(_entrada.AddDays(-40)).Handle(Comando(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(EstadoResultado.NoEncontrado, resultado.Estado);
        Assert.Empty(_outbox.Encolados);
    }

    [Fact]
    public async Task Solicitud_duplicada_propaga_409_sin_encolar_segundo_evento()
    {
        var reserva = SembrarConfirmada();
        await CrearHandler(_entrada.AddDays(-40)).Handle(Comando(reserva.Id), CancellationToken.None);

        await Assert.ThrowsAsync<TransicionEstadoInvalidaException>(() =>
            CrearHandler(_entrada.AddDays(-5)).Handle(Comando(reserva.Id), CancellationToken.None));

        Assert.Single(_outbox.Encolados); // solo el de la 1ª solicitud
    }

    [Fact]
    public async Task Estancia_iniciada_propaga_409_sin_encolar()
    {
        var reserva = SembrarConfirmada();

        await Assert.ThrowsAsync<TransicionEstadoInvalidaException>(() =>
            CrearHandler(_entrada).Handle(Comando(reserva.Id), CancellationToken.None)); // hoy == entrada

        Assert.Equal(EstadoReserva.Confirmada, reserva.Estado);
        Assert.Empty(_outbox.Encolados);
    }
}
