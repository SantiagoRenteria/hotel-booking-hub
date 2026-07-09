using HotelBookingHub.Comun.Eventos;
using Notificaciones.Worker.Notificaciones;

namespace Notificaciones.UnitTests;

/// <summary>
/// Story 5.2 (AC-E5.2.1) — al consumir <c>SolicitudCancelacionRegistrada.v1</c>, el worker avisa al viajero con
/// un acuse que incluye la penalidad ETIQUETADA como estimación (no cobro final) y al agente con un aviso "por
/// resolver". Reutiliza el inbox idempotente (5.1b): N entregas → 1 correo por destinatario.
/// </summary>
public sealed class ConsumidorSolicitudCancelacionTests
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

    private static EventoIntegracion Envelope(
        string? huespedEmail = "andres@example.com",
        string? agenteEmail = "carolina@agencia.com",
        decimal penalidad = 25m,
        Guid? id = null) => new(
        Id: id ?? Guid.CreateVersion7(),
        Type: SolicitudCancelacionRegistradaV1.Tipo,
        Version: 1,
        OccurredAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        TraceId: null,
        Data: new SolicitudCancelacionRegistradaV1(
            AggregateId: Guid.CreateVersion7(),
            Iniciador: "Viajero",
            MotivoCategoria: "CambioDePlanes",
            MotivoDetalle: "El viajero ya no puede asistir.",
            PenalidadPorcentaje: penalidad,
            FechaSolicitud: new DateOnly(2026, 8, 20),
            HuespedEmail: huespedEmail,
            AgenteEmail: agenteEmail));

    private static ConsumidorSolicitudCancelacion Crear(INotificador noti) =>
        new(noti, new InboxIdempotenciaEnMemoria());

    [Fact]
    public async Task Avisa_al_viajero_con_la_penalidad_etiquetada_como_estimacion()
    {
        var fake = new NotificadorFake();
        await Crear(fake).ProcesarAsync(Envelope(penalidad: 25m), CancellationToken.None);

        var viajero = Assert.Single(fake.Enviados, c => c.Destinatario == "andres@example.com");
        Assert.Contains("25", viajero.Cuerpo); // incluye el porcentaje estimado
        Assert.Contains("estimación", viajero.Cuerpo, StringComparison.OrdinalIgnoreCase); // etiquetado como estimación
        Assert.DoesNotContain("cobro final", viajero.Cuerpo, StringComparison.OrdinalIgnoreCase); // no es el cobro final
    }

    [Fact]
    public async Task Avisa_al_agente_de_que_hay_algo_por_resolver()
    {
        var fake = new NotificadorFake();
        await Crear(fake).ProcesarAsync(Envelope(), CancellationToken.None);

        var agente = Assert.Single(fake.Enviados, c => c.Destinatario == "carolina@agencia.com");
        Assert.Contains("por resolver", agente.Cuerpo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Otro_tipo_de_evento_se_ignora()
    {
        var fake = new NotificadorFake();
        var evento = Envelope() with { Type = "OtroEvento.v1" };

        await Crear(fake).ProcesarAsync(evento, CancellationToken.None);

        Assert.Empty(fake.Enviados);
    }

    [Fact]
    public async Task Entregado_N_veces_envia_un_correo_por_destinatario()
    {
        var fake = new NotificadorFake();
        var consumidor = Crear(fake);
        var evento = Envelope();

        for (var i = 0; i < 4; i++)
        {
            await consumidor.ProcesarAsync(evento, CancellationToken.None);
        }

        Assert.Equal(2, fake.Enviados.Count); // viajero + agente, exactamente una vez cada uno
    }

    [Fact]
    public async Task Sin_email_de_huesped_no_notifica_al_viajero_pero_si_al_agente()
    {
        var fake = new NotificadorFake();
        await Crear(fake).ProcesarAsync(Envelope(huespedEmail: null), CancellationToken.None);

        Assert.DoesNotContain(fake.Enviados, c => c.Destinatario == "andres@example.com");
        Assert.Contains(fake.Enviados, c => c.Destinatario == "carolina@agencia.com");
    }
}
