namespace Hoteles.Domain.Habitaciones;

/// <summary>
/// Aggregate root de la habitación (unidad reservable), <b>independiente</b> del <c>Hotel</c>: se gestiona y
/// versiona por separado (AC-E2.4.2), referenciándolo por <see cref="HotelId"/> (no es un owned type). Mismas
/// claves ADR-017 que <c>Hotel</c> (UUID v7 PK no-clustered + <c>Seq</c> shadow clustered + <c>rowversion</c>).
/// <see cref="CostoBase"/>/<see cref="Impuestos"/> son atributos de catálogo (montos ≥ 0); el cálculo del
/// precio de una reserva vive en el BC de Reservas.
/// </summary>
public sealed class Habitacion
{
    public Guid Id { get; private set; }
    public Guid HotelId { get; private set; }
    public string Tipo { get; private set; } = string.Empty;
    public decimal CostoBase { get; private set; }
    public decimal Impuestos { get; private set; }
    public string Ubicacion { get; private set; } = string.Empty;
    public EstadoHabitacion Estado { get; private set; }

    private Habitacion() { } // EF Core

    public static Habitacion Crear(
        Guid hotelId, string tipo, decimal costoBase, decimal impuestos, string ubicacion, EstadoHabitacion estado)
    {
        if (hotelId == Guid.Empty)
        {
            throw new ArgumentException("El hotel de la habitación es obligatorio.", nameof(hotelId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);
        ArgumentException.ThrowIfNullOrWhiteSpace(ubicacion);
        ArgumentOutOfRangeException.ThrowIfNegative(costoBase);
        ArgumentOutOfRangeException.ThrowIfNegative(impuestos);

        return new Habitacion
        {
            Id = Guid.CreateVersion7(),
            HotelId = hotelId,
            Tipo = tipo.Trim(),
            CostoBase = costoBase,
            Impuestos = impuestos,
            Ubicacion = ubicacion.Trim(),
            Estado = estado,
        };
    }

    /// <summary>
    /// Actualiza los datos de la habitación (AC-E2.4.2). NO cambia el <see cref="Estado"/> (transición dedicada,
    /// como en el hotel) ni el <see cref="HotelId"/> (una habitación no se «mueve» de hotel).
    /// </summary>
    public void Editar(string tipo, decimal costoBase, decimal impuestos, string ubicacion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);
        ArgumentException.ThrowIfNullOrWhiteSpace(ubicacion);
        ArgumentOutOfRangeException.ThrowIfNegative(costoBase);
        ArgumentOutOfRangeException.ThrowIfNegative(impuestos);

        Tipo = tipo.Trim();
        CostoBase = costoBase;
        Impuestos = impuestos;
        Ubicacion = ubicacion.Trim();
    }

    /// <summary>Habilita la habitación (idempotente). Única vía de transición junto con <see cref="Deshabilitar"/>.</summary>
    public void Habilitar() => Estado = EstadoHabitacion.Habilitada;

    /// <summary>Deshabilita la habitación (idempotente): deja de ofertarse.</summary>
    public void Deshabilitar() => Estado = EstadoHabitacion.Deshabilitada;
}
