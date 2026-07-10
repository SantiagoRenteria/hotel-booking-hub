namespace Reservas.Domain.Reservas;

/// <summary>
/// Value Object inmutable de la penalidad de cancelación como <b>sugerencia congelada</b> (AC-E4.1.1): un
/// porcentaje (0–100) fijado en la fecha de solicitud. NO se cobra ni se recalcula al resolver (Story 4.2);
/// el monto informativo se deriva sobre el precio ya congelado de la reserva vía <see cref="MontoSobre"/>.
/// </summary>
public sealed record PenalidadSugerida
{
    public decimal Porcentaje { get; }

    private PenalidadSugerida(decimal porcentaje) => Porcentaje = porcentaje;

    public static PenalidadSugerida Crear(decimal porcentaje)
    {
        if (porcentaje is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(porcentaje), porcentaje, "El porcentaje de penalidad debe estar entre 0 y 100.");
        }

        return new PenalidadSugerida(porcentaje);
    }

    /// <summary>Monto informativo de la penalidad sobre un precio total ya congelado (2 decimales, banker's rounding).</summary>
    public decimal MontoSobre(decimal precioTotal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(precioTotal);
        return Math.Round(precioTotal * Porcentaje / 100m, 2, MidpointRounding.ToEven);
    }
}
