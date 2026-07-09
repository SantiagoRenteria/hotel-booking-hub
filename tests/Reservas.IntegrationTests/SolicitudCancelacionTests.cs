using System.Text.Json;
using System.Text.Json.Nodes;
using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reservas.Application.Reservas.SolicitarCancelacion;
using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;
using Reservas.Infrastructure;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Story 4.1 (Task 6) — acceptance/BDD end-to-end sobre SQL Server real (Testcontainers), reutilizando el
/// write-path de producción (mediator → Validation → Transaction → handler → EjecutorTransaccional → SQL).
/// Cubre: happy path (transición + penalidad CONGELADA persistida + evento en el outbox en la MISMA tx),
/// congelación del 100% con &lt;30 días, y los AC negativos (duplicada / estancia iniciada → 409 sin evento).
/// El reloj es un <see cref="RelojFijoIntegracion"/> inyectado para que la fecha de solicitud sea determinista.
/// </summary>
[Collection("sqlserver")]
public sealed class SolicitudCancelacionTests(SqlServerFixture fixture)
{
    private static readonly DateOnly _hoy = new(2026, 7, 1);

    private ServiceProvider Contenedor()
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddSingleton<CalculadorPrecio>();
        servicios.AddSingleton<CalculadorPenalidad>();
        servicios.AddSingleton<TimeProvider>(new RelojFijoIntegracion(_hoy));
        servicios.AddMediatorPipeline(typeof(SolicitarCancelacionCommand).Assembly);
        servicios.AddReservasInfrastructure(fixture.CadenaConexion);
        return servicios.BuildServiceProvider();
    }

    private async Task<Guid> SembrarConfirmadaAsync(DateOnly entrada, decimal precio = 500m)
    {
        var reserva = Reserva.Crear(Guid.NewGuid(), Estancia.Crear(entrada, entrada.AddDays(2)), precioTotal: precio);
        await using var db = fixture.CrearContexto();
        db.Reservas.Add(reserva);
        await db.SaveChangesAsync();
        return reserva.Id;
    }

    private static SolicitarCancelacionCommand Comando(Guid id) =>
        new(id, "CambioDePlanes", "El viajero ya no puede asistir.", IniciadorCancelacion.Viajero);

    private async Task<Result> EnviarAsync(Guid id)
    {
        await using var proveedor = Contenedor();
        using var scope = proveedor.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var r = await sender.Send(Comando(id), CancellationToken.None);
        return new Result(r.EsExitoso, r.Valor);
    }

    private readonly record struct Result(bool EsExitoso, SolicitudCancelacionResponseDto? Valor);

    // AC-E4.1.1 — Given confirmada (≥30 días) → When solicita → Then CancelacionSolicitada + 0% congelado + evento.
    [Fact]
    public async Task Solicitud_valida_transiciona_congela_penalidad_y_escribe_outbox()
    {
        var entrada = _hoy.AddDays(40); // ≥30 días → 0%
        var id = await SembrarConfirmadaAsync(entrada);

        var resultado = await EnviarAsync(id);

        Assert.True(resultado.EsExitoso);
        Assert.Equal("CancelacionSolicitada", resultado.Valor!.Estado);
        Assert.Equal(0m, resultado.Valor.PenalidadPorcentaje);

        await using var verify = fixture.CrearContexto();
        var reserva = await verify.Reservas.SingleAsync(r => r.Id == id);
        Assert.Equal(EstadoReserva.CancelacionSolicitada, reserva.Estado);
        Assert.NotNull(reserva.SolicitudCancelacion);
        Assert.Equal(0m, reserva.SolicitudCancelacion!.PenalidadPorcentaje); // congelada en BD
        Assert.Equal(_hoy, reserva.SolicitudCancelacion.FechaSolicitud);
        Assert.Equal(IniciadorCancelacion.Viajero, reserva.SolicitudCancelacion.IniciadaPor);

        var eventos = await verify.OutboxMessages.Where(o => o.AggregateId == id).ToListAsync();
        var outbox = Assert.Single(eventos);
        Assert.Equal(SolicitudCancelacionRegistradaV1.Tipo, outbox.Type);
        var data = JsonNode.Parse(outbox.Payload)!["data"]!.AsObject();
        Assert.Equal(0m, data["penalidadPorcentaje"]!.GetValue<decimal>());
        Assert.Equal("2026-07-01", (string?)data["fechaSolicitud"]);
    }

    // AC-E4.1.1 — la penalidad del 100% (<30 días) también queda CONGELADA en BD y en el evento.
    [Fact]
    public async Task Penalidad_cien_por_ciento_se_congela_cuando_faltan_menos_de_treinta_dias()
    {
        var entrada = _hoy.AddDays(10); // <30 días → 100%
        var id = await SembrarConfirmadaAsync(entrada, precio: 500m);

        var resultado = await EnviarAsync(id);

        Assert.Equal(100m, resultado.Valor!.PenalidadPorcentaje);
        Assert.Equal(500m, resultado.Valor.PenalidadMonto);

        await using var verify = fixture.CrearContexto();
        var reserva = await verify.Reservas.SingleAsync(r => r.Id == id);
        Assert.Equal(100m, reserva.SolicitudCancelacion!.PenalidadPorcentaje);
    }

    // AC-E4.1.2 — 2ª solicitud (ya no está Confirmada) → 409 (TransicionEstadoInvalidaException) y sin 2º evento.
    [Fact]
    public async Task Solicitud_duplicada_devuelve_conflicto_y_no_escribe_segundo_evento()
    {
        var id = await SembrarConfirmadaAsync(_hoy.AddDays(40));
        await EnviarAsync(id); // 1ª solicitud OK

        await Assert.ThrowsAsync<TransicionEstadoInvalidaException>(() => EnviarAsync(id));

        await using var verify = fixture.CrearContexto();
        Assert.Equal(1, await verify.OutboxMessages.CountAsync(o => o.AggregateId == id)); // solo el 1º
        var reserva = await verify.Reservas.SingleAsync(r => r.Id == id);
        Assert.Equal(EstadoReserva.CancelacionSolicitada, reserva.Estado);
    }

    // AC-E4.1.3 — estancia ya iniciada (fechaSolicitud == entrada) → 409 y la reserva NO muta ni escribe evento.
    [Fact]
    public async Task Estancia_iniciada_no_es_elegible_y_no_escribe_outbox()
    {
        var id = await SembrarConfirmadaAsync(_hoy); // entrada == hoy → estancia iniciada

        await Assert.ThrowsAsync<TransicionEstadoInvalidaException>(() => EnviarAsync(id));

        await using var verify = fixture.CrearContexto();
        var reserva = await verify.Reservas.SingleAsync(r => r.Id == id);
        Assert.Equal(EstadoReserva.Confirmada, reserva.Estado);
        Assert.Null(reserva.SolicitudCancelacion);
        Assert.Equal(0, await verify.OutboxMessages.CountAsync(o => o.AggregateId == id));
    }
}

/// <summary>Reloj fijo para tests de integración: fecha determinista para congelar la penalidad.</summary>
internal sealed class RelojFijoIntegracion(DateOnly hoy) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(hoy.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
