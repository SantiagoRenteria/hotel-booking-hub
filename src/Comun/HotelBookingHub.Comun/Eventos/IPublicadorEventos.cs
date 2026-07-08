namespace HotelBookingHub.Comun.Eventos;

/// <summary>
/// Puerto de publicación de eventos de integración. El dominio/aplicación depende de esta
/// abstracción; el adaptador concreto (Dapr pub/sub) y el outbox transaccional que lo respalda
/// se implementan en la Story 1.6b / Épica 5. Es la costura donde se enchufa el transporte real.
/// </summary>
public interface IPublicadorEventos
{
    /// <summary>Publica un evento en el tópico indicado.</summary>
    Task PublicarAsync(string topico, EventoIntegracion evento, CancellationToken ct);
}
