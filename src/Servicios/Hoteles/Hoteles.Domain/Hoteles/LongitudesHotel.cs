namespace Hoteles.Domain.Hoteles;

/// <summary>
/// Longitudes máximas de los campos de texto del hotel. <b>Fuente única</b> compartida por los validators de
/// aplicación (→ 400 si se excede) y por el mapeo EF (<c>HasMaxLength</c>). Sin este tope común, un valor
/// sobredimensionado podría pasar la validación y reventar en el INSERT/UPDATE (truncamiento → 500) en vez de
/// devolver 400 — o divergir entre alta y edición.
/// </summary>
public static class LongitudesHotel
{
    public const int Nombre = 200;
    public const int Ciudad = 120;
    public const int Direccion = 300;
    public const int Descripcion = 2000;

    /// <summary>Agente propietario (Story 6.3): email en forma canónica; tope holgado y acorde a un email.</summary>
    public const int AgentePropietario = 320;
}
