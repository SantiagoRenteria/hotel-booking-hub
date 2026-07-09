using System.Globalization;
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

    public async Task ProcesarAsync(EventoIntegracion evento, CancellationToken ct)
    {
        // Solo reacciona a la confirmación de reserva; otros eventos se ignoran silenciosamente.
        if (evento.Type != ReservaConfirmadaV1.Tipo)
        {
            return;
        }

        // Guard: un payload malformado/nulo se rechaza con un error DESCRIPTIVO (Id/Type), no con un NRE opaco.
        // La política de reintento/dead-letter ante veneno es de 5.1b.
        var data = Deserializar(evento)
            ?? throw new InvalidOperationException(
                $"Evento {evento.Id} ({evento.Type}) sin data deserializable a {nameof(ReservaConfirmadaV1)}.");

        var estancia = $"{data.Entrada:yyyy-MM-dd} → {data.Salida:yyyy-MM-dd}";
        // InvariantCulture: el importe del correo NO debe variar con la config regional del host.
        var total = data.PrecioTotal.ToString("0.00", CultureInfo.InvariantCulture);

        await notificador.NotificarAsync(
            data.HuespedEmail,
            $"Reserva confirmada en {data.HotelNombre}",
            $"Hola {data.HuespedNombre}, tu reserva en {data.HotelNombre} ({data.Ciudad}) está confirmada para {estancia}. Total: {total}.",
            ct);

        await notificador.NotificarAsync(
            data.AgenteEmail,
            $"Reserva confirmada: {data.HotelNombre}",
            $"La reserva de {data.HuespedNombre} en {data.HotelNombre} ({data.Ciudad}) para {estancia} quedó confirmada. Total: {total}.",
            ct);
    }

    /// <summary>Tras el transporte, <c>Data</c> llega como <c>JsonElement</c>; en invocación directa como el tipo.
    /// Devuelve <c>null</c> si el payload no es deserializable (el llamador decide cómo tratarlo).</summary>
    private static ReservaConfirmadaV1? Deserializar(EventoIntegracion evento) =>
        evento.Data is ReservaConfirmadaV1 tipado
            ? tipado
            : JsonSerializer.Deserialize<ReservaConfirmadaV1>(JsonSerializer.Serialize(evento.Data, _opciones), _opciones);
}
