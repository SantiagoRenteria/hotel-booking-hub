namespace HotelBookingHub.Comun.Resultados;

/// <summary>
/// Sobre de paginación (offset) para listados: los <see cref="Items"/> de la página pedida más los metadatos
/// (<see cref="Page"/> 1-based, <see cref="PageSize"/>, <see cref="Total"/> de elementos disponibles). Permite al
/// cliente navegar sin traer todo el conjunto de golpe (Story T.6). Genérico y reutilizable entre BCs.
/// </summary>
public sealed record PaginaDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
