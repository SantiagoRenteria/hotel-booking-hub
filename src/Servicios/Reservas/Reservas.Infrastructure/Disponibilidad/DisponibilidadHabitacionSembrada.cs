using Reservas.Domain.Puertos;

namespace Reservas.Infrastructure.Disponibilidad;

/// <summary>
/// Placeholder de <see cref="IDisponibilidadHabitacion"/> para el happy path de 1.6a: trata cualquier
/// habitación solicitada como activa, con datos de hotel y precios de catálogo demo. En E3 lo reemplaza
/// la proyección de disponibilidad real (Redis + eventos), que además filtrará por fechas.
/// </summary>
public sealed class DisponibilidadHabitacionSembrada : IDisponibilidadHabitacion
{
    // Valores demo de catálogo (dinero en decimal). No son secretos ni configuración sensible.
    private const decimal CostoBaseDemo = 100.00m;
    private const decimal ImpuestoDemo = 19.00m;

    public Task<HabitacionReservable?> ObtenerAsync(Guid habitacionId, CancellationToken ct)
    {
        var habitacion = new HabitacionReservable(
            Id: habitacionId,
            HotelId: Guid.Empty,
            HotelNombre: "Hotel Demo",
            Ciudad: "Bogotá",
            Activa: true,
            Capacidad: 4,
            CostoBase: CostoBaseDemo,
            Impuesto: ImpuestoDemo);

        return Task.FromResult<HabitacionReservable?>(habitacion);
    }
}
