using HotelBookingHub.Comun.Eventos;
using Microsoft.Extensions.Logging.Abstractions;
using Reservas.Infrastructure.Mensajeria;

namespace Reservas.IntegrationTests;

/// <summary>
/// Story 9.1 (AC-E9.1.5) — el adaptador RabbitMQ PROPAGA la excepción si el broker no está disponible (no la
/// traga): así el <c>ProcesadorOutbox</c> deja la fila <c>Pendiente</c> y la re-reclama (at-least-once). No
/// requiere contenedor: se apunta a un broker inalcanzable y se verifica que <c>PublicarAsync</c> lanza.
/// </summary>
public sealed class TransportePublicadorRabbitMqTests
{
    private static EventoIntegracion Evento() => new(
        Id: Guid.CreateVersion7(),
        Type: ReservaConfirmadaV1.Tipo,
        Version: 1,
        OccurredAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        TraceId: null,
        Data: new object());

    [Fact]
    public async Task Publicar_con_broker_inalcanzable_propaga_la_excepcion()
    {
        // Given: un adaptador apuntando a un puerto donde no hay broker (conexión rechazada de inmediato).
        await using var publicador = new PublicadorEventosRabbitMq(
            "amqp://guest:guest@127.0.0.1:1", NullLogger<PublicadorEventosRabbitMq>.Instance);

        // When / Then: PublicarAsync NO traga el fallo → propaga (el ProcesadorOutbox lo captura y deja Pendiente).
        await Assert.ThrowsAnyAsync<Exception>(() =>
            publicador.PublicarAsync("reservas", Evento(), CancellationToken.None));
    }
}
