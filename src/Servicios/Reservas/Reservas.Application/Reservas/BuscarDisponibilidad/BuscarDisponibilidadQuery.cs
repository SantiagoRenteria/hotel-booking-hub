using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;

namespace Reservas.Application.Reservas.BuscarDisponibilidad;

/// <summary>
/// Query de lectura (CQRS) de la puerta de entrada del viajero (AC-E3.2.1): busca habitaciones disponibles por
/// ciudad, rango semiabierto <c>[Entrada, Salida)</c> y número de huéspedes. Es <see cref="IRequest{T}"/> y NO
/// <c>ICommand</c> → no pasa por el <c>TransactionBehavior</c> (sin transacción ni outbox). Se sirve desde la
/// <c>ProyeccionHabitacion</c> (read-model de 3.1) más los slots de disponibilidad locales, nunca del catálogo de
/// Hoteles. Es best-effort (consistencia eventual): a lo sumo produce 409 evitables, nunca overbooking.
/// </summary>
public sealed record BuscarDisponibilidadQuery(
    string Ciudad,
    DateOnly Entrada,
    DateOnly Salida,
    int Huespedes) : IRequest<Result<IReadOnlyList<HabitacionDisponibleDto>>>;

/// <summary>
/// Proyección de una habitación disponible devuelta por la búsqueda (AC-E3.2.1). Solo datos de catálogo del
/// read-model; el precio total por estancia lo calcula el flujo de reserva, no la búsqueda.
/// </summary>
public sealed record HabitacionDisponibleDto(
    Guid HabitacionId,
    Guid HotelId,
    string Ciudad,
    string Tipo,
    string Ubicacion,
    int Capacidad,
    decimal CostoBase,
    decimal Impuestos);
