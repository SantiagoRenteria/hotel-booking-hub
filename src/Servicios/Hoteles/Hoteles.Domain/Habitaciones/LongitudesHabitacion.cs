namespace Hoteles.Domain.Habitaciones;

/// <summary>
/// Topes de los campos de la habitación. <b>Fuente única</b> compartida por los validators de aplicación
/// (→ 400 si se excede) y por el mapeo EF (<c>HasMaxLength</c>/<c>HasPrecision</c>), como <c>LongitudesHotel</c>.
/// Incluye la precisión monetaria: sin la cota superior, un monto con más dígitos de los que caben en
/// <c>decimal(18,2)</c> pasaría la validación y reventaría en el INSERT/UPDATE (overflow → 500) en vez de 400;
/// y más de 2 decimales se redondearían en silencio (respuesta ≠ persistido).
/// </summary>
public static class LongitudesHabitacion
{
    public const int Tipo = 100;
    public const int Ubicacion = 200;

    /// <summary>Dígitos totales de los montos de catálogo (costo base, impuestos). Debe coincidir con el mapeo EF.</summary>
    public const int PrecisionMonto = 18;

    /// <summary>Decimales de los montos de catálogo. Debe coincidir con el mapeo EF.</summary>
    public const int EscalaMonto = 2;
}
