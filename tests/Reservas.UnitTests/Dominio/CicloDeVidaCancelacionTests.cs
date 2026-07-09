using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;
using Xunit;

namespace Reservas.UnitTests.Dominio;

/// <summary>
/// Story 4.3 (Task 2, AC-E4.3.2) — matriz del ciclo de vida: las ÚNICAS transiciones permitidas son
/// <c>Confirmada → CancelacionSolicitada → {Cancelada | Confirmada}</c>. Toda transición fuera del ciclo
/// (p. ej. <c>Cancelada → *</c>, o <c>Confirmada → Cancelada</c> directo sin solicitud) se rechaza por guard
/// (<see cref="TransicionEstadoInvalidaException"/>). Cierra el invariante del ciclo a nivel de dominio.
/// </summary>
public sealed class CicloDeVidaCancelacionTests
{
    private static readonly DateOnly _entrada = new(2026, 9, 1);
    private readonly CalculadorPenalidad _calc = new();

    private static MotivoCancelacion Motivo() => MotivoCancelacion.Crear("CambioDePlanes", "detalle");

    private Reserva Confirmada() => Reserva.Crear(Guid.NewGuid(), Estancia.Crear(_entrada, _entrada.AddDays(2)), precioTotal: 500m);

    private Reserva Cancelada()
    {
        var r = Confirmada();
        r.SolicitarCancelacion(Motivo(), IniciadorCancelacion.Viajero, _entrada.AddDays(-40), _calc);
        r.Resolver(DecisionCancelacion.AprobarAplicandoPenalidad, "agente@hotel.com", _entrada.AddDays(-5), null);
        return r;
    }

    // --- Transiciones PERMITIDAS (no lanzan) ---

    [Fact]
    public void Confirmada_a_solicitada_a_cancelada_es_valido()
    {
        var r = Confirmada();
        r.SolicitarCancelacion(Motivo(), IniciadorCancelacion.Viajero, _entrada.AddDays(-40), _calc);
        r.Resolver(DecisionCancelacion.AprobarAplicandoPenalidad, "agente@hotel.com", _entrada.AddDays(-5), null);
        Assert.Equal(EstadoReserva.Cancelada, r.Estado);
    }

    [Fact]
    public void Confirmada_a_solicitada_a_confirmada_por_rechazo_es_valido()
    {
        var r = Confirmada();
        r.SolicitarCancelacion(Motivo(), IniciadorCancelacion.Viajero, _entrada.AddDays(-40), _calc);
        r.Resolver(DecisionCancelacion.Rechazar, "agente@hotel.com", _entrada.AddDays(-5), "No procede.");
        Assert.Equal(EstadoReserva.Confirmada, r.Estado);
    }

    // --- Transiciones PROHIBIDAS (lanzan y no mutan) ---

    [Fact]
    public void Confirmada_a_cancelada_directo_sin_solicitud_se_rechaza()
    {
        var r = Confirmada();
        Assert.Throws<TransicionEstadoInvalidaException>(() =>
            r.Resolver(DecisionCancelacion.AprobarAplicandoPenalidad, "agente@hotel.com", _entrada.AddDays(-5), null));
        Assert.Equal(EstadoReserva.Confirmada, r.Estado);
    }

    [Fact]
    public void Cancelada_no_puede_volver_a_solicitar()
    {
        var r = Cancelada();
        Assert.Throws<TransicionEstadoInvalidaException>(() =>
            r.SolicitarCancelacion(Motivo(), IniciadorCancelacion.Viajero, _entrada.AddDays(-3), _calc));
        Assert.Equal(EstadoReserva.Cancelada, r.Estado);
    }

    [Fact]
    public void Cancelada_no_puede_resolverse_de_nuevo()
    {
        var r = Cancelada();
        Assert.Throws<TransicionEstadoInvalidaException>(() =>
            r.Resolver(DecisionCancelacion.Rechazar, "agente@hotel.com", _entrada.AddDays(-3), "x"));
        Assert.Equal(EstadoReserva.Cancelada, r.Estado);
        Assert.Empty(r.Noches); // sigue liberada, no se re-crea inventario
    }

    [Fact]
    public void Solicitada_no_puede_re_solicitarse()
    {
        var r = Confirmada();
        r.SolicitarCancelacion(Motivo(), IniciadorCancelacion.Viajero, _entrada.AddDays(-40), _calc);
        Assert.Throws<TransicionEstadoInvalidaException>(() =>
            r.SolicitarCancelacion(Motivo(), IniciadorCancelacion.Viajero, _entrada.AddDays(-3), _calc));
        Assert.Equal(EstadoReserva.CancelacionSolicitada, r.Estado);
    }
}
