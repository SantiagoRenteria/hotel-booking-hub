namespace Reservas.Domain.Reservas;

/// <summary>
/// Value Object del motivo de una cancelación: <see cref="Categoria"/> (clasificación) + <see cref="Detalle"/>
/// (texto libre). Ambos obligatorios (AC-E4.1.1). La validación de entrada la aplica el <c>ValidationBehavior</c>
/// (→ 400); este guard es defensa en profundidad del dominio (coherente con <see cref="Estancia"/>).
/// </summary>
public sealed record MotivoCancelacion
{
    public string Categoria { get; }
    public string Detalle { get; }

    private MotivoCancelacion(string categoria, string detalle)
    {
        Categoria = categoria;
        Detalle = detalle;
    }

    public static MotivoCancelacion Crear(string categoria, string detalle)
    {
        if (string.IsNullOrWhiteSpace(categoria))
        {
            throw new ArgumentException("La categoría del motivo de cancelación es obligatoria.", nameof(categoria));
        }

        if (string.IsNullOrWhiteSpace(detalle))
        {
            throw new ArgumentException("El detalle del motivo de cancelación es obligatorio.", nameof(detalle));
        }

        return new MotivoCancelacion(categoria.Trim(), detalle.Trim());
    }
}
