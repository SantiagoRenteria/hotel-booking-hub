using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Excepciones;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.CrearReserva;
using Reservas.Application.Reservas.ResolverCancelacion;
using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;
using Reservas.Infrastructure;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Story 4.2 (Task 6) — acceptance/BDD end-to-end sobre SQL Server real. Incluye <b>el assert que importa</b>
/// (AC-E4.2.1): aprobar libera el slot y una NUEVA reserva sobre esa noche vuelve a caber (201, round-trip con el
/// motor anti-overbooking de E1). Además: rechazar no toca slots; doble resolución concurrente → 1×2xx/1×409 y
/// <c>count(slots)</c> no sube a 2 (money-test style); agente ajeno → 403.
/// </summary>
[Collection("sqlserver")]
public sealed class ResolverCancelacionTests(SqlServerFixture fixture)
{
    private const string Dueno = "agente@hotel.com";
    private static readonly DateOnly _hoy = new(2026, 7, 1);

    private sealed class ContextoAgenteFijo(string? agente) : IContextoAgente
    {
        public string? AgenteActual { get; } = agente;
    }

    private ServiceProvider Contenedor(string? agente)
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddSingleton<CalculadorPrecio>();
        servicios.AddSingleton<CalculadorPenalidad>();
        servicios.AddSingleton<TimeProvider>(new RelojFijoIntegracion(_hoy));
        servicios.AddSingleton<IContextoAgente>(new ContextoAgenteFijo(agente));
        servicios.AddMediatorPipeline(typeof(ResolverCancelacionCommand).Assembly);
        servicios.AddReservasInfrastructure($"{fixture.CadenaConexion};Max Pool Size=200;");
        return servicios.BuildServiceProvider();
    }

    /// <summary>Persiste una reserva de 1 noche ya en CancelacionSolicitada, propiedad de <paramref name="dueno"/>.</summary>
    private async Task<(Guid Id, Guid Habitacion)> SembrarSolicitadaAsync(string dueno, DateOnly entrada)
    {
        var habitacion = Guid.NewGuid();
        var reserva = Reserva.Crear(habitacion, Estancia.Crear(entrada, entrada.AddDays(1)), agenteEmail: dueno, precioTotal: 500m);
        reserva.SolicitarCancelacion(
            MotivoCancelacion.Crear("CambioDePlanes", "detalle"),
            IniciadorCancelacion.Viajero,
            _hoy, // >30 días respecto a las entradas usadas → penalidad 0% (irrelevante para el round-trip)
            new CalculadorPenalidad());

        await using var db = fixture.CrearContexto();
        db.Reservas.Add(reserva);
        await db.SaveChangesAsync();
        return (reserva.Id, habitacion);
    }

    private async Task<int> ContarNochesAsync(Guid habitacion)
    {
        await using var db = fixture.CrearContexto();
        return await db.NochesHabitacion.CountAsync(n => n.HabitacionId == habitacion);
    }

    // AC-E4.2.1 — EL ASSERT QUE IMPORTA: aprobar libera el slot y una nueva reserva sobre [D] responde 201.
    [Fact]
    public async Task Aprobar_libera_el_slot_y_una_nueva_reserva_sobre_esa_noche_cabe()
    {
        var entrada = new DateOnly(2026, 10, 1);
        var (id, habitacion) = await SembrarSolicitadaAsync(Dueno, entrada);

        await using var proveedor = Contenedor(Dueno);

        using (var scope = proveedor.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var r = await sender.Send(new ResolverCancelacionCommand(id, DecisionCancelacion.AprobarAplicandoPenalidad, null), CancellationToken.None);
            Assert.True(r.EsExitoso);
            Assert.Equal("Cancelada", r.Valor!.Estado);
        }

        Assert.Equal(0, await ContarNochesAsync(habitacion)); // slot liberado
        await using (var db = fixture.CrearContexto())
        {
            Assert.Equal(EstadoReserva.Cancelada, (await db.Reservas.SingleAsync(x => x.Id == id)).Estado);
            Assert.Equal(1, await db.OutboxMessages.CountAsync(o => o.AggregateId == id && o.Type == ReservaCanceladaV1.Tipo));
        }

        // Round-trip: la misma noche vuelve a caber (motor anti-overbooking de E1) → 201.
        using (var scope = proveedor.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var nueva = new ReservaTestDataBuilder().ParaHabitacion(habitacion).ConEstancia(entrada, entrada.AddDays(1)).Construir();
            var r = await sender.Send(nueva, CancellationToken.None);
            Assert.True(r.EsExitoso); // 201: el slot estaba libre
        }
    }

    // AC-E4.2.2 — rechazar no toca slots y vuelve a Confirmada.
    [Fact]
    public async Task Rechazar_no_libera_slots_y_vuelve_a_confirmada()
    {
        var entrada = new DateOnly(2026, 10, 5);
        var (id, habitacion) = await SembrarSolicitadaAsync(Dueno, entrada);

        await using var proveedor = Contenedor(Dueno);
        using (var scope = proveedor.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var r = await sender.Send(new ResolverCancelacionCommand(id, DecisionCancelacion.Rechazar, "No procede."), CancellationToken.None);
            Assert.Equal("Confirmada", r.Valor!.Estado);
        }

        Assert.Equal(1, await ContarNochesAsync(habitacion)); // slot intacto
        await using var db = fixture.CrearContexto();
        Assert.Equal(1, await db.OutboxMessages.CountAsync(o => o.AggregateId == id && o.Type == SolicitudCancelacionRechazadaV1.Tipo));
    }

    // AC-E4.2.3 — doble resolución concurrente: exactamente 1 aprueba, 1×409, y count(slots) NO sube a 2 (queda 0).
    [Fact]
    public async Task Doble_resolucion_concurrente_produce_una_cancelada_y_un_conflicto()
    {
        var entrada = new DateOnly(2026, 10, 10);
        var (id, habitacion) = await SembrarSolicitadaAsync(Dueno, entrada);

        await using var proveedor = Contenedor(Dueno);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            using var scope = proveedor.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            try
            {
                var r = await sender.Send(new ResolverCancelacionCommand(id, DecisionCancelacion.AprobarAplicandoPenalidad, null), CancellationToken.None);
                return r.EsExitoso ? "ok" : "inesperado";
            }
            catch (ConflictoConcurrenciaException) { return "409"; }   // perdió la carrera del rowVersion
            catch (TransicionEstadoInvalidaException) { return "409"; } // o cargó después del commit (guard de estado)
            catch (Exception) { return "inesperado"; }
        }));

        Assert.Equal(1, resultados.Count(r => r == "ok"));
        Assert.Equal(1, resultados.Count(r => r == "409"));
        Assert.Equal(0, resultados.Count(r => r == "inesperado"));

        Assert.Equal(0, await ContarNochesAsync(habitacion)); // liberado UNA vez; no sube a 2
        await using var db = fixture.CrearContexto();
        Assert.Equal(EstadoReserva.Cancelada, (await db.Reservas.SingleAsync(x => x.Id == id)).Estado);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(o => o.AggregateId == id)); // exactamente un evento
    }

    // AC-E4.2.4 — agente ajeno → 403, sin mutar la reserva.
    [Fact]
    public async Task Agente_ajeno_no_puede_resolver()
    {
        var entrada = new DateOnly(2026, 10, 15);
        var (id, habitacion) = await SembrarSolicitadaAsync(Dueno, entrada);

        await using var proveedor = Contenedor("otro@agencia.com");
        using var scope = proveedor.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var r = await sender.Send(new ResolverCancelacionCommand(id, DecisionCancelacion.AprobarAplicandoPenalidad, null), CancellationToken.None);

        Assert.Equal(EstadoResultado.Prohibido, r.Estado);
        Assert.Equal(1, await ContarNochesAsync(habitacion)); // intacto
        await using var db = fixture.CrearContexto();
        Assert.Equal(EstadoReserva.CancelacionSolicitada, (await db.Reservas.SingleAsync(x => x.Id == id)).Estado);
        Assert.Equal(0, await db.OutboxMessages.CountAsync(o => o.AggregateId == id));
    }
}
