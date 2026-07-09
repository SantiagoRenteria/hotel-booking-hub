namespace HotelBookingHub.Comun.Mensajeria;

/// <summary>
/// Marca una petición como comando (escritura). SOLO los comandos pasan por el <c>TransactionBehavior</c>
/// (transacción + outbox + retry 1205); las queries (<see cref="IRequest{TResponse}"/> sin este marcador) no.
/// </summary>
public interface ICommand<TResponse> : IRequest<TResponse>;
