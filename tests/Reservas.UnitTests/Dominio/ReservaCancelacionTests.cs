using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;
using Xunit;

namespace Reservas.UnitTests.Dominio;

/// <summary>
/// Story 4.1 — máquina de estados de la cancelación en el agregado <see cref="Reserva"/> (Task 1), con estilo
/// BDD (Given/When/Then). Cubre la transición feliz + congelación de la penalidad y la TABLA de transiciones
/// prohibidas (duplicada, estancia iniciada) que deben lanzar <see cref="TransicionEstadoInvalidaException"/>
/// (→ 409) SIN mutar el agregado. Guards por excepción de dominio (decisión party-mode Task 0).
/// </summary>
public sealed class ReservaCancelacionTests
{
    private static readonly Guid _habitacion = Guid.NewGuid();
    private readonly CalculadorPenalidad _calculador = new();

    private static Reserva ReservaConfirmada(DateOnly entrada, decimal precioTotal = 0m) =>
        Reserva.Crear(_habitacion, Estancia.Crear(entrada, entrada.AddDays(2)), precioTotal: precioTotal);

    private static MotivoCancelacion Motivo() => MotivoCancelacion.Crear("CambioDePlanes", "El viajero ya no puede asistir.");

    [Fact]
    public void Dado_confirmada_y_no_iniciada_cuando_solicita_entonces_transiciona_y_congela_penalidad()
    {
        var entrada = new DateOnly(2026, 9, 1);
        var fechaSolicitud = entrada.AddDays(-40); // >30 días → 0%
        var reserva = ReservaConfirmada(entrada, precioTotal: 500m);

        var penalidad = reserva.SolicitarCancelacion(Motivo(), IniciadorCancelacion.Viajero, fechaSolicitud, _calculador);

        Assert.Equal(EstadoReserva.CancelacionSolicitada, reserva.Estado);
        Assert.Equal(0m, penalidad.Porcentaje);
        Assert.NotNull(reserva.SolicitudCancelacion);
        Assert.Equal(0m, reserva.SolicitudCancelacion!.PenalidadPorcentaje); // congelada en el agregado
        Assert.Equal(fechaSolicitud, reserva.SolicitudCancelacion.FechaSolicitud);
        Assert.Equal(IniciadorCancelacion.Viajero, reserva.SolicitudCancelacion.IniciadaPor);
        Assert.Equal("CambioDePlanes", reserva.SolicitudCancelacion.MotivoCategoria);
    }

    [Fact]
    public void Dado_menos_de_treinta_dias_cuando_solicita_entonces_penalidad_del_cien_por_ciento()
    {
        var entrada = new DateOnly(2026, 9, 1);
        var reserva = ReservaConfirmada(entrada);

        var penalidad = reserva.SolicitarCancelacion(Motivo(), IniciadorCancelacion.Agente, entrada.AddDays(-10), _calculador);

        Assert.Equal(100m, penalidad.Porcentaje);
    }

    // AC-E4.1.2 — una 2ª solicitud (ya no está Confirmada) es una transición prohibida → 409 y sin re-congelar.
    [Fact]
    public void Dado_solicitud_en_curso_cuando_solicita_otra_entonces_transicion_invalida()
    {
        var entrada = new DateOnly(2026, 9, 1);
        var reserva = ReservaConfirmada(entrada);
        reserva.SolicitarCancelacion(Motivo(), IniciadorCancelacion.Viajero, entrada.AddDays(-40), _calculador);
        var penalidadCongelada = reserva.SolicitudCancelacion!.PenalidadPorcentaje;

        var ex = Assert.Throws<TransicionEstadoInvalidaException>(() =>
            reserva.SolicitarCancelacion(Motivo(), IniciadorCancelacion.Viajero, entrada.AddDays(-5), _calculador));

        Assert.Equal(EstadoReserva.CancelacionSolicitada, ex.Origen);
        Assert.Equal(EstadoReserva.CancelacionSolicitada, reserva.Estado); // no cambia
        Assert.Equal(penalidadCongelada, reserva.SolicitudCancelacion!.PenalidadPorcentaje); // no se re-congela
    }

    // AC-E4.1.3 — estancia ya iniciada (fechaSolicitud >= entrada) = no elegible → 409, NO penalidad 100%.
    [Theory]
    [InlineData(0)]   // el mismo día de la entrada
    [InlineData(1)]   // estancia ya en curso
    public void Dado_estancia_iniciada_cuando_solicita_entonces_transicion_invalida(int diasDespuesDeEntrada)
    {
        var entrada = new DateOnly(2026, 9, 1);
        var reserva = ReservaConfirmada(entrada);

        Assert.Throws<TransicionEstadoInvalidaException>(() =>
            reserva.SolicitarCancelacion(Motivo(), IniciadorCancelacion.Viajero, entrada.AddDays(diasDespuesDeEntrada), _calculador));

        Assert.Equal(EstadoReserva.Confirmada, reserva.Estado); // no muta
        Assert.Null(reserva.SolicitudCancelacion);
    }

    // Víspera de la entrada (fechaSolicitud = entrada - 1): SÍ es elegible (estancia no iniciada) → 100%.
    [Fact]
    public void Dado_vispera_de_la_entrada_cuando_solicita_entonces_es_elegible_con_cien_por_ciento()
    {
        var entrada = new DateOnly(2026, 9, 1);
        var reserva = ReservaConfirmada(entrada);

        var penalidad = reserva.SolicitarCancelacion(Motivo(), IniciadorCancelacion.Viajero, entrada.AddDays(-1), _calculador);

        Assert.Equal(EstadoReserva.CancelacionSolicitada, reserva.Estado);
        Assert.Equal(100m, penalidad.Porcentaje);
    }
}
