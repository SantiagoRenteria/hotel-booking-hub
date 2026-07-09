namespace Hoteles.Domain.Habitaciones;

/// <summary>Estado de publicación de la habitación. El toggle habilitar/deshabilitar se gestiona por operación dedicada (2.4).</summary>
public enum EstadoHabitacion
{
    Habilitada = 1,
    Deshabilitada = 2,
}
