namespace Reservas.Domain.Reservas;

/// <summary>
/// Value Object del rango de fechas de una reserva, semiabierto <c>[Entrada, Salida)</c>:
/// la noche de <see cref="Salida"/> no se reserva (dos estancias adyacentes no se solapan).
/// Se construye solo por <see cref="Crear"/>, que garantiza el invariante del rango.
/// </summary>
public sealed record Estancia
{
    public DateOnly Entrada { get; }
    public DateOnly Salida { get; }

    /// <summary>Número de noches del rango semiabierto: días entre entrada (incl.) y salida (excl.).</summary>
    public int Noches => Salida.DayNumber - Entrada.DayNumber;

    private Estancia(DateOnly entrada, DateOnly salida)
    {
        Entrada = entrada;
        Salida = salida;
    }

    /// <summary>Crea una estancia válida o lanza <see cref="EstanciaInvalidaException"/> si el rango no es coherente.</summary>
    public static Estancia Crear(DateOnly entrada, DateOnly salida)
    {
        if (salida <= entrada)
        {
            throw new EstanciaInvalidaException("La fecha de salida debe ser posterior a la de entrada");
        }

        return new Estancia(entrada, salida);
    }
}
