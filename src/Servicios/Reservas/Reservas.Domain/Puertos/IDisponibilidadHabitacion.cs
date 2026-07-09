namespace Reservas.Domain.Puertos;

/// <summary>
/// Puerto de consulta de la habitación a reservar. En 1.6a lo satisface un dato sembrado (stub);
/// en E3 lo respaldará la proyección de disponibilidad (Redis + eventos). El handler solo depende del puerto.
/// </summary>
public interface IDisponibilidadHabitacion
{
    Task<HabitacionReservable?> ObtenerAsync(Guid habitacionId, CancellationToken ct);
}

/// <summary>
/// Vista mínima de una habitación para poder reservarla: estado, capacidad, precios del catálogo y los
/// datos del hotel que el evento <c>ReservaConfirmada</c> necesita (nombre, ciudad).
/// </summary>
public sealed record HabitacionReservable(
    Guid Id,
    Guid HotelId,
    string HotelNombre,
    string Ciudad,
    bool Activa,
    int Capacidad,
    decimal CostoBase,
    decimal Impuesto);
