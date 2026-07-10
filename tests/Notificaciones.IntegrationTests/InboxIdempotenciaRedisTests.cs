using Notificaciones.Worker.Notificaciones;
using Xunit;

namespace Notificaciones.IntegrationTests;

/// <summary>
/// Story 5.1b (AC-E5.1b.1, Task 1) — semántica <c>SETNX</c>+TTL del inbox sobre Redis REAL: la primera reserva
/// gana, las siguientes se descartan; liberar permite re-reservar; la reserva expira por TTL.
/// </summary>
[Collection("Redis")]
public sealed class InboxIdempotenciaRedisTests(RedisFixture fixture)
{
    private InboxIdempotenciaRedis Crear(TimeSpan? ttl = null) =>
        new(fixture.Redis, new OpcionesInbox(ttl ?? TimeSpan.FromMinutes(10)));

    [Fact]
    public async Task Primera_reserva_gana_y_la_segunda_se_descarta()
    {
        var inbox = Crear();
        var id = Guid.CreateVersion7();

        Assert.True(await inbox.IntentarMarcarProcesadoAsync(id, 1, "huesped", CancellationToken.None));
        Assert.False(await inbox.IntentarMarcarProcesadoAsync(id, 1, "huesped", CancellationToken.None));
    }

    [Fact]
    public async Task Distinto_efecto_no_colisiona_con_la_misma_clave_de_mensaje()
    {
        var inbox = Crear();
        var id = Guid.CreateVersion7();

        // El mismo (MessageId, version) con efecto distinto son reservas independientes (1 por destinatario).
        Assert.True(await inbox.IntentarMarcarProcesadoAsync(id, 1, "huesped", CancellationToken.None));
        Assert.True(await inbox.IntentarMarcarProcesadoAsync(id, 1, "agente", CancellationToken.None));
    }

    [Fact]
    public async Task Liberar_permite_volver_a_reservar()
    {
        var inbox = Crear();
        var id = Guid.CreateVersion7();

        Assert.True(await inbox.IntentarMarcarProcesadoAsync(id, 1, "huesped", CancellationToken.None));
        await inbox.LiberarAsync(id, 1, "huesped", CancellationToken.None);

        // Tras liberar (envío fallido), la re-entrega puede volver a reservar y reintentar.
        Assert.True(await inbox.IntentarMarcarProcesadoAsync(id, 1, "huesped", CancellationToken.None));
    }

    [Fact]
    public async Task La_reserva_expira_por_TTL()
    {
        var inbox = Crear(ttl: TimeSpan.FromSeconds(1));
        var id = Guid.CreateVersion7();

        Assert.True(await inbox.IntentarMarcarProcesadoAsync(id, 1, "huesped", CancellationToken.None));
        // Margen amplio sobre el TTL de 1s para no flakear bajo carga de contenedores en paralelo (CI).
        await Task.Delay(TimeSpan.FromMilliseconds(2500));

        // Expirada la clave, una re-entrega tardía puede re-reservar (el TTL debe superar la ventana del broker).
        Assert.True(await inbox.IntentarMarcarProcesadoAsync(id, 1, "huesped", CancellationToken.None));
    }
}
