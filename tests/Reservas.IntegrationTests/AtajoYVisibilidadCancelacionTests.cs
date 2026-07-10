using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.CancelarEnUnPaso;
using Reservas.Application.Reservas.ListarCancelacionesPendientes;
using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;
using Reservas.Infrastructure;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Story 4.3 (Task 5) — acceptance end-to-end. El atajo valida el OUTBOX MULTI-EVENTO en BD real (dos filas con
/// MessageId distinto, sin violar UNIQUE) + libera el slot al aprobar; el rechazo emite el par correcto sin
/// ReservaCancelada. La visibilidad expone "días en espera" aislada por agente, sin expiración.
/// </summary>
[Collection("sqlserver")]
public sealed class AtajoYVisibilidadCancelacionTests(SqlServerFixture fixture)
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
        servicios.AddMediatorPipeline(typeof(CancelarEnUnPasoCommand).Assembly);
        servicios.AddReservasInfrastructure($"{fixture.CadenaConexion};Max Pool Size=200;");
        return servicios.BuildServiceProvider();
    }

    private async Task<Guid> SembrarConfirmadaAsync(DateOnly entrada, string dueno = Dueno)
    {
        var reserva = Reserva.Crear(Guid.NewGuid(), Estancia.Crear(entrada, entrada.AddDays(1)), agenteEmail: dueno, precioTotal: 500m);
        await using var db = fixture.CrearContexto();
        db.Reservas.Add(reserva);
        await db.SaveChangesAsync();
        return reserva.Id;
    }

    // AC-E4.3.1 — atajo aprobando: DOS eventos por el outbox multi-evento (MessageId distinto) + slot liberado.
    [Fact]
    public async Task Atajo_aprobando_emite_ambos_eventos_en_el_outbox_y_libera_el_slot()
    {
        var entrada = new DateOnly(2026, 12, 1);
        var id = await SembrarConfirmadaAsync(entrada);

        await using var proveedor = Contenedor(Dueno);
        using (var scope = proveedor.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var r = await sender.Send(
                new CancelarEnUnPasoCommand(id, "CambioDePlanes", "detalle", IniciadorCancelacion.Agente, DecisionCancelacion.AprobarAplicandoPenalidad, null),
                CancellationToken.None);
            Assert.True(r.EsExitoso);
            Assert.Equal("Cancelada", r.Valor!.Estado);
        }

        await using var db = fixture.CrearContexto();
        var eventos = await db.OutboxMessages.Where(o => o.AggregateId == id).OrderBy(o => o.Seq).ToListAsync();
        Assert.Equal(2, eventos.Count); // multi-evento en una tx, sin violar UNIQUE(MessageId)
        Assert.Equal(SolicitudCancelacionRegistradaV1.Tipo, eventos[0].Type);
        Assert.Equal(ReservaCanceladaV1.Tipo, eventos[1].Type);
        Assert.Equal(2, eventos.Select(e => e.MessageId).Distinct().Count()); // MessageId por-mensaje distinto
        Assert.Equal(0, await db.NochesHabitacion.CountAsync(n => n.ReservaId == id)); // slot liberado
    }

    // AC-E4.3.1 — atajo rechazando: par {Registrada, Rechazada} y NUNCA ReservaCancelada.
    [Fact]
    public async Task Atajo_rechazando_emite_el_par_correcto_sin_ReservaCancelada()
    {
        var entrada = new DateOnly(2026, 12, 5);
        var id = await SembrarConfirmadaAsync(entrada);

        await using var proveedor = Contenedor(Dueno);
        using (var scope = proveedor.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var r = await sender.Send(
                new CancelarEnUnPasoCommand(id, "CambioDePlanes", "detalle", IniciadorCancelacion.Agente, DecisionCancelacion.Rechazar, "No procede."),
                CancellationToken.None);
            Assert.Equal("Confirmada", r.Valor!.Estado);
        }

        await using var db = fixture.CrearContexto();
        var tipos = await db.OutboxMessages.Where(o => o.AggregateId == id).Select(o => o.Type).ToListAsync();
        Assert.Equal(2, tipos.Count);
        Assert.Contains(SolicitudCancelacionRegistradaV1.Tipo, tipos);
        Assert.Contains(SolicitudCancelacionRechazadaV1.Tipo, tipos);
        Assert.DoesNotContain(ReservaCanceladaV1.Tipo, tipos);
        Assert.Equal(1, await db.NochesHabitacion.CountAsync(n => n.ReservaId == id)); // slot intacto
    }

    // AC-E4.3.3 — visibilidad: "días en espera" correctos y aislada por agente (no ve las de otro).
    [Fact]
    public async Task Visibilidad_expone_dias_en_espera_aislada_por_agente()
    {
        // Agente ÚNICO para este test: la colección "sqlserver" es compartida y otros tests siembran
        // pendientes con el agente por defecto; aislar por un email propio evita el ruido cruzado.
        const string agente = "visibilidad-agente@hotel.com";

        // Pendiente del agente, solicitada hace 7 días; y una de OTRO agente que no debe verse.
        var idMio = await SembrarSolicitadaAsync(agente, _hoy.AddDays(-7), new DateOnly(2026, 12, 20));
        await SembrarSolicitadaAsync("visibilidad-otro@agencia.com", _hoy.AddDays(-3), new DateOnly(2026, 12, 21));

        await using var proveedor = Contenedor(agente);
        using var scope = proveedor.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var r = await sender.Send(new ListarCancelacionesPendientesQuery(), CancellationToken.None);

        Assert.True(r.EsExitoso);
        var item = Assert.Single(r.Valor!); // solo la del dueño (aislamiento)
        Assert.Equal(idMio, item.ReservaId);
        Assert.Equal(7, item.DiasEnEspera);
    }

    private async Task<Guid> SembrarSolicitadaAsync(string dueno, DateOnly fechaSolicitud, DateOnly entrada)
    {
        var reserva = Reserva.Crear(Guid.NewGuid(), Estancia.Crear(entrada, entrada.AddDays(1)), agenteEmail: dueno, precioTotal: 500m);
        reserva.SolicitarCancelacion(MotivoCancelacion.Crear("CambioDePlanes", "detalle"), IniciadorCancelacion.Viajero, fechaSolicitud, new CalculadorPenalidad());
        await using var db = fixture.CrearContexto();
        db.Reservas.Add(reserva);
        await db.SaveChangesAsync();
        return reserva.Id;
    }
}
