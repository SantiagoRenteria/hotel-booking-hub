namespace HotelBookingHub.Comun.Mensajeria;

/// <summary>Marcador de una petición (command/query) que el mediator despacha y cuya respuesta es <typeparamref name="TResponse"/>.</summary>
public interface IRequest<TResponse>;
