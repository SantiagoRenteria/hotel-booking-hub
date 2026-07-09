using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;
using Xunit;

namespace Reservas.UnitTests.Dominio;

/// <summary>
/// Story 4.2 (Task 1+2) — resolución de la cancelación en el agregado (BDD). Cubre: aprobar (aplicar/condonar) →
/// <see cref="EstadoReserva.Cancelada"/> + LIBERACIÓN de slots + auditoría (default/override); rechazar → vuelve a
/// <see cref="EstadoReserva.Confirmada"/> sin tocar slots; y la tabla de transiciones prohibidas (doble resolución,
/// resolver algo no solicitado) → <see cref="TransicionEstadoInvalidaException"/> sin mutar el agregado.
/// </summary>
public sealed class ReservaResolucionTests
{
    private static readonly Guid _habitacion = Guid.NewGuid();
    private static readonly DateOnly _entrada = new(2026, 9, 1);
    private readonly CalculadorPenalidad _calculador = new();

    private static MotivoCancelacion Motivo() => MotivoCancelacion.Crear("CambioDePlanes", "detalle");

    /// <summary>Reserva de 2 noches en CancelacionSolicitada con la penalidad congelada del caso.</summary>
    private Reserva ReservaSolicitada(int diasAntelacion)
    {
        var reserva = Reserva.Crear(_habitacion, Estancia.Crear(_entrada, _entrada.AddDays(2)), precioTotal: 500m);
        reserva.SolicitarCancelacion(Motivo(), IniciadorCancelacion.Viajero, _entrada.AddDays(-diasAntelacion), _calculador);
        return reserva;
    }

    [Fact]
    public void Dado_solicitada_cuando_aprueba_aplicando_entonces_cancela_libera_slots_y_audita()
    {
        var reserva = ReservaSolicitada(diasAntelacion: 10); // <30 → penalidad congelada 100%
        var quien = "agente@hotel.com";

        reserva.Resolver(DecisionCancelacion.AprobarAplicandoPenalidad, quien, _entrada.AddDays(-5), motivo: null);

        Assert.Equal(EstadoReserva.Cancelada, reserva.Estado);
        Assert.Empty(reserva.Noches); // slot liberado (invariante anti-overbooking)
        var s = reserva.SolicitudCancelacion!;
        Assert.Equal(ResultadoResolucion.Aprobada, s.Resultado);
        Assert.Equal(100m, s.PenalidadAplicadaPorcentaje);
        Assert.False(s.PenalidadFueOverride); // aplicó la sugerida congelada
        Assert.Equal(quien, s.ResueltaPor);
    }

    [Fact]
    public void Dado_solicitada_cuando_aprueba_condonando_entonces_penalidad_cero_y_override()
    {
        var reserva = ReservaSolicitada(diasAntelacion: 10); // sugerida 100%

        reserva.Resolver(DecisionCancelacion.AprobarCondonandoPenalidad, "agente@hotel.com", _entrada.AddDays(-5), null);

        Assert.Equal(EstadoReserva.Cancelada, reserva.Estado);
        Assert.Empty(reserva.Noches);
        var s = reserva.SolicitudCancelacion!;
        Assert.Equal(0m, s.PenalidadAplicadaPorcentaje);
        Assert.True(s.PenalidadFueOverride); // condonó 100→0
    }

    [Fact]
    public void Dado_solicitada_cuando_condona_una_penalidad_ya_cero_entonces_no_es_override()
    {
        var reserva = ReservaSolicitada(diasAntelacion: 40); // sugerida 0%

        reserva.Resolver(DecisionCancelacion.AprobarCondonandoPenalidad, "agente@hotel.com", _entrada.AddDays(-35), null);

        Assert.False(reserva.SolicitudCancelacion!.PenalidadFueOverride); // 0→0 no es override
    }

    [Fact]
    public void Dado_solicitada_cuando_rechaza_con_motivo_entonces_vuelve_a_confirmada_sin_tocar_slots()
    {
        var reserva = ReservaSolicitada(diasAntelacion: 10);

        reserva.Resolver(DecisionCancelacion.Rechazar, "agente@hotel.com", _entrada.AddDays(-5), "No procede la cancelación.");

        Assert.Equal(EstadoReserva.Confirmada, reserva.Estado);
        Assert.Equal(2, reserva.Noches.Count); // NO se liberan slots
        var s = reserva.SolicitudCancelacion!;
        Assert.Equal(ResultadoResolucion.Rechazada, s.Resultado);
        Assert.Equal("No procede la cancelación.", s.MotivoResolucion);
    }

    // AC-E4.2.3 — una 2ª resolución (ya no está CancelacionSolicitada) → transición prohibida sin re-liberar.
    [Fact]
    public void Dado_ya_resuelta_cuando_resuelve_de_nuevo_entonces_transicion_invalida()
    {
        var reserva = ReservaSolicitada(diasAntelacion: 10);
        reserva.Resolver(DecisionCancelacion.AprobarAplicandoPenalidad, "agente@hotel.com", _entrada.AddDays(-5), null);

        Assert.Throws<TransicionEstadoInvalidaException>(() =>
            reserva.Resolver(DecisionCancelacion.AprobarAplicandoPenalidad, "agente@hotel.com", _entrada.AddDays(-4), null));

        Assert.Equal(EstadoReserva.Cancelada, reserva.Estado); // no muta
        Assert.Empty(reserva.Noches);
    }

    [Fact]
    public void Dado_confirmada_sin_solicitud_cuando_resuelve_entonces_transicion_invalida()
    {
        var reserva = Reserva.Crear(_habitacion, Estancia.Crear(_entrada, _entrada.AddDays(2)));

        Assert.Throws<TransicionEstadoInvalidaException>(() =>
            reserva.Resolver(DecisionCancelacion.Rechazar, "agente@hotel.com", _entrada.AddDays(-5), "x"));

        Assert.Equal(EstadoReserva.Confirmada, reserva.Estado);
        Assert.Equal(2, reserva.Noches.Count);
    }
}
