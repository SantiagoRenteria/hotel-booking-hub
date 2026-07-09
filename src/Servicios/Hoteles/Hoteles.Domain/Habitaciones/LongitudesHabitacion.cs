namespace Hoteles.Domain.Habitaciones;

/// <summary>
/// Longitudes máximas de los campos de texto de la habitación. <b>Fuente única</b> compartida por los validators
/// de aplicación (→ 400 si se excede) y por el mapeo EF (<c>HasMaxLength</c>), como <c>LongitudesHotel</c>.
/// </summary>
public static class LongitudesHabitacion
{
    public const int Tipo = 100;
    public const int Ubicacion = 200;
}
