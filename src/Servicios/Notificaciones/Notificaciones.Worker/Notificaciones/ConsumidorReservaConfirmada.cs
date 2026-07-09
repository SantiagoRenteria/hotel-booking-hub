using System.Text.Json;
using HotelBookingHub.Comun.Eventos;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Consumidor del evento <c>ReservaConfirmada.v1</c> (Story 5.1a). Recibe el envelope
/// <see cref="EventoIntegracion"/> tal cual (el <c>data</c> puede llegar como <c>JsonElement</c> tras el
/// transporte, patrón del consumidor de catálogo de 3.1) y emite DOS correos: uno al huésped y otro al agente.
/// Sin dedup todavía (la idempotencia del consumidor es Story 5.1b).
/// </summary>
public sealed class ConsumidorReservaConfirmada(INotificador notificador)
{
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);

    public Task ProcesarAsync(EventoIntegracion evento, CancellationToken ct)
    {
        _ = (notificador, Deserializar(evento), ct);
        throw new NotImplementedException();
    }

    /// <summary>Tras el transporte, <c>Data</c> llega como <c>JsonElement</c>; en invocación directa como el tipo.</summary>
    private static ReservaConfirmadaV1 Deserializar(EventoIntegracion evento) =>
        evento.Data is ReservaConfirmadaV1 tipado
            ? tipado
            : JsonSerializer.Deserialize<ReservaConfirmadaV1>(JsonSerializer.Serialize(evento.Data, _opciones), _opciones)!;
}
