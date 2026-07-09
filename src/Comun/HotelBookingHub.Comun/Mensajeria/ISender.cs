namespace HotelBookingHub.Comun.Mensajeria;

/// <summary>Punto de entrada del mediator: despacha una petición por su pipeline hasta el handler.</summary>
public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct);
}
