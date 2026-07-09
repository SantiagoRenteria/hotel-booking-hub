namespace Reservas.Infrastructure.Proyeccion;

/// <summary>
/// Read-model del estado de publicación de un hotel (Story 3.2, cierre de AC-E3.2.2/FR-7), propiedad de Reservas,
/// alimentado por los eventos <c>HotelHabilitado</c>/<c>HotelDeshabilitado</c> de Hoteles. Es una <b>dimensión
/// independiente</b> de <see cref="ProyeccionHabitacion"/> (field-ownership: el estado del hotel y el de la
/// habitación tienen dueños distintos y no se colapsan): la búsqueda excluye las habitaciones cuyo hotel esté
/// inactivo, y al rehabilitar el hotel NO resucita habitaciones deshabilitadas individualmente.
/// <para>Convergencia bajo desorden/reentrega por <b>LWW</b> sobre <see cref="VersionEstado"/>. Una fila puede
/// crearse por un evento de hotel que llega ANTES del alta de sus habitaciones (huérfana, correcta por join).</para>
/// </summary>
public sealed class ProyeccionHotelEstado
{
    public Guid HotelId { get; private set; }

    /// <summary>True = el hotel oferta; false = deshabilitado. Por defecto activo (un hotel sin eventos oferta).</summary>
    public bool Activo { get; private set; }

    /// <summary>Versión del último evento de estado aplicado (LWW; descarta eventos viejos/repetidos).</summary>
    public int VersionEstado { get; private set; }

    private ProyeccionHotelEstado() { } // EF Core

    /// <summary>Fila nueva: por defecto <see cref="Activo"/> = true (aún sin evento de estado aplicado).</summary>
    public static ProyeccionHotelEstado Nueva(Guid hotelId) => new() { HotelId = hotelId, Activo = true };

    /// <summary>Aplica <c>HotelDeshabilitado</c>: LWW por <see cref="VersionEstado"/>. Devuelve true si mutó.</summary>
    public bool AplicarDeshabilitado(int version)
    {
        if (version <= VersionEstado)
        {
            return false;
        }

        Activo = false;
        VersionEstado = version;
        return true;
    }

    /// <summary>Aplica <c>HotelHabilitado</c>: LWW por <see cref="VersionEstado"/>. Devuelve true si mutó.</summary>
    public bool AplicarHabilitado(int version)
    {
        if (version <= VersionEstado)
        {
            return false;
        }

        Activo = true;
        VersionEstado = version;
        return true;
    }
}
