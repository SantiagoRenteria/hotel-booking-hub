using HotelBookingHub.Comun.Eventos;
using Notificaciones.Worker.Notificaciones;

namespace Notificaciones.UnitTests;

/// <summary>
/// Story 5.3 (AC-E5.3.1/.2) — al consumir la resolución de una cancelación, el worker notifica al viajero el
/// desenlace FINAL: penalidad aplicada o condonación (`ReservaCancelada.v1`), o rechazo con "sigue Confirmada" +
/// motivo (`SolicitudCancelacionRechazada.v1`). Invariante: un rechazo NUNCA comunica cancelación y viceversa.
/// </summary>
public sealed class ConsumidorResolucionCancelacionTests
{
    private sealed record CorreoEnviado(string Destinatario, string Asunto, string Cuerpo);

    private sealed class NotificadorFake : INotificador
    {
        public List<CorreoEnviado> Enviados { get; } = [];

        public Task NotificarAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct)
        {
            Enviados.Add(new CorreoEnviado(destinatario, asunto, cuerpo));
            return Task.CompletedTask;
        }
    }

    private const string Viajero = "andres@example.com";

    private static EventoIntegracion Cancelada(
        decimal aplicada, bool override_, string? huespedEmail = Viajero, Guid? id = null) => new(
        Id: id ?? Guid.CreateVersion7(),
        Type: ReservaCanceladaV1.Tipo,
        Version: 1,
        OccurredAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        TraceId: null,
        Data: new ReservaCanceladaV1(
            AggregateId: Guid.CreateVersion7(),
            ResueltaPor: "agente@hotel.com",
            FechaResolucion: new DateOnly(2026, 8, 20),
            PenalidadAplicadaPorcentaje: aplicada,
            PenalidadFueOverride: override_,
            HuespedEmail: huespedEmail));

    private static EventoIntegracion Rechazada(string motivo, string? huespedEmail = Viajero, Guid? id = null) => new(
        Id: id ?? Guid.CreateVersion7(),
        Type: SolicitudCancelacionRechazadaV1.Tipo,
        Version: 1,
        OccurredAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        TraceId: null,
        Data: new SolicitudCancelacionRechazadaV1(
            AggregateId: Guid.CreateVersion7(),
            ResueltaPor: "agente@hotel.com",
            FechaResolucion: new DateOnly(2026, 8, 20),
            MotivoRechazo: motivo,
            HuespedEmail: huespedEmail));

    private static ConsumidorResolucionCancelacion Crear(INotificador noti) =>
        new(noti, new InboxIdempotenciaEnMemoria());

    [Fact]
    public async Task Aprobacion_aplicando_comunica_la_penalidad_final()
    {
        var fake = new NotificadorFake();
        await Crear(fake).ProcesarAsync(Cancelada(aplicada: 100m, override_: false), CancellationToken.None);

        var correo = Assert.Single(fake.Enviados);
        Assert.Equal(Viajero, correo.Destinatario);
        Assert.Contains("100", correo.Cuerpo);
        Assert.Contains("final", correo.Cuerpo, StringComparison.OrdinalIgnoreCase); // penalidad FINAL, no estimación
        Assert.DoesNotContain("estimación", correo.Cuerpo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Condonacion_comunica_que_la_penalidad_fue_condonada()
    {
        var fake = new NotificadorFake();
        await Crear(fake).ProcesarAsync(Cancelada(aplicada: 0m, override_: true), CancellationToken.None);

        var correo = Assert.Single(fake.Enviados);
        Assert.Contains("condon", correo.Cuerpo, StringComparison.OrdinalIgnoreCase); // condonada/condonación
    }

    [Fact]
    public async Task Aprobacion_con_override_indica_el_ajuste_del_agente()
    {
        var fake = new NotificadorFake();
        await Crear(fake).ProcesarAsync(Cancelada(aplicada: 50m, override_: true), CancellationToken.None);

        var correo = Assert.Single(fake.Enviados);
        Assert.Contains("50", correo.Cuerpo);
        Assert.Contains("agente", correo.Cuerpo, StringComparison.OrdinalIgnoreCase); // nota de ajuste del agente
    }

    [Fact]
    public async Task Rechazo_comunica_que_sigue_confirmada_con_el_motivo()
    {
        var fake = new NotificadorFake();
        await Crear(fake).ProcesarAsync(Rechazada("No procede por temporada alta."), CancellationToken.None);

        var correo = Assert.Single(fake.Enviados);
        Assert.Equal(Viajero, correo.Destinatario);
        Assert.Contains("Confirmada", correo.Cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No procede por temporada alta.", correo.Cuerpo);
        // Invariante: un rechazo NO comunica cancelación (no habla de penalidad aplicada).
        Assert.DoesNotContain("penalidad", correo.Cuerpo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Entregado_N_veces_envia_un_solo_correo()
    {
        var fake = new NotificadorFake();
        var consumidor = Crear(fake);
        var evento = Cancelada(aplicada: 100m, override_: false);

        for (var i = 0; i < 4; i++)
        {
            await consumidor.ProcesarAsync(evento, CancellationToken.None);
        }

        Assert.Single(fake.Enviados);
    }

    [Fact]
    public async Task Otro_tipo_de_evento_se_ignora()
    {
        var fake = new NotificadorFake();
        var evento = Cancelada(aplicada: 100m, override_: false) with { Type = "OtroEvento.v1" };

        await Crear(fake).ProcesarAsync(evento, CancellationToken.None);

        Assert.Empty(fake.Enviados);
    }

    [Fact]
    public async Task Sin_email_de_huesped_no_envia()
    {
        var fake = new NotificadorFake();
        await Crear(fake).ProcesarAsync(Rechazada("x", huespedEmail: null), CancellationToken.None);

        Assert.Empty(fake.Enviados);
    }
}
