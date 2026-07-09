using HotelBookingHub.Comun.Eventos;

namespace Reservas.Application.Abstracciones;

/// <summary>
/// Puerto consumidor de los eventos de catálogo de Hoteles (Story 3.1). Recibe el envelope
/// <see cref="EventoIntegracion"/> tal cual (el <c>data</c> llega como <c>JsonElement</c> tras el transporte) y
/// actualiza la <c>ProyeccionHabitacion</c> de forma idempotente y ordenada. La implementación deduplica por
/// <see cref="EventoIntegracion.Id"/> (inbox) y aplica el evento en la MISMA transacción SQL (sin dual-write).
/// El transporte concreto (bus fake en tests; Dapr pub/sub en producción) es un detalle externo a este puerto.
/// </summary>
public interface IConsumidorEventosCatalogo
{
    Task ProcesarAsync(EventoIntegracion evento, CancellationToken ct);
}
