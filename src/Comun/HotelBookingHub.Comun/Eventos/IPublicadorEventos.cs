namespace HotelBookingHub.Comun.Eventos;

/// <summary>
/// Puerto de publicación de eventos de integración. El dominio/aplicación depende
/// de esta abstracción; el adaptador concreto (Dapr pub/sub) vive en infraestructura.
/// Es la costura donde se enchufa el transporte real: en el esqueleto (Story 1.1) se
/// usa una implementación en memoria para probar el patrón; el adaptador Dapr y el
/// outbox transaccional llegan en 1.6b / E5.
/// </summary>
public interface IPublicadorEventos
{
    /// <summary>Publica un evento en el tópico indicado.</summary>
    Task PublicarAsync(string topico, EventoIntegracion evento, CancellationToken ct);
}
