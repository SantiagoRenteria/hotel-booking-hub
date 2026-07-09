namespace Reservas.Application.Reservas.CrearReserva;

/// <summary>
/// Recurso devuelto al confirmar la reserva (201): identidad UUID v7, estado y precio total (AC-E1.6a.4).
/// Sin envoltura <c>{data}</c> y sin exponer identificadores internos (p. ej. <c>Seq</c>).
/// </summary>
public sealed record ReservaResponseDto(Guid Id, string Estado, decimal PrecioTotal);
