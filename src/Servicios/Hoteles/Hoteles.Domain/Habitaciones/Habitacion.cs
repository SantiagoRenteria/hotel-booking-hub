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

    /// <summary>
    /// Capacidad de huéspedes de la habitación (atributo de catálogo, &gt; 0). La búsqueda de disponibilidad
    /// (E3.2) filtra por <c>capacidad &gt;= huéspedes</c>. Viaja como estado en los eventos de catálogo (no tiene
    /// evento propio): editarla no emite ni versiona por sí sola.
    /// </summary>
    public int Capacidad { get; private set; }

    /// <summary>
    /// Versión de secuencia del agregado (Story 2.5, AC-E2.5.4): parte monotónica del order key
    /// <c>(HabitacionId, Version)</c> de los eventos de catálogo. Cuenta SOLO las mutaciones que EMITEN evento
    /// (alta, cambio real de precio, deshabilitación); las que no emiten (editar sin tocar el precio, habilitar,
    /// idempotentes) no la incrementan, de modo que la secuencia emitida es estrictamente creciente y contigua.
    /// Es independiente del <c>rowversion</c> (concurrencia optimista): miden cosas distintas y coexisten.
    /// </summary>
    public int Version { get; private set; }

    private Habitacion() { } // EF Core

    public static Habitacion Crear(
        Guid hotelId, string tipo, decimal costoBase, decimal impuestos, string ubicacion, EstadoHabitacion estado,
        int capacidad)
    {
        if (hotelId == Guid.Empty)
        {
            throw new ArgumentException("El hotel de la habitación es obligatorio.", nameof(hotelId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);
        ArgumentException.ThrowIfNullOrWhiteSpace(ubicacion);
        ArgumentOutOfRangeException.ThrowIfNegative(costoBase);
        ArgumentOutOfRangeException.ThrowIfNegative(impuestos);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacidad);

        return new Habitacion
        {
            Id = Guid.CreateVersion7(),
            HotelId = hotelId,
            Tipo = tipo.Trim(),
            CostoBase = costoBase,
            Impuestos = impuestos,
            Ubicacion = ubicacion.Trim(),
            Estado = estado,
            Capacidad = capacidad,
            Version = 1, // el alta es el primer evento del agregado (HabitacionAgregada.v1)
        };
    }

    /// <summary>
    /// Actualiza los datos de la habitación (AC-E2.4.2). NO cambia el <see cref="Estado"/> (transición dedicada,
    /// como en el hotel) ni el <see cref="HotelId"/> (una habitación no se «mueve» de hotel).
    /// <para>
    /// Devuelve <c>true</c> si cambió el PRECIO (<see cref="CostoBase"/> o <see cref="Impuestos"/>), único
    /// disparador de <c>PrecioHabitacionCambiado</c> (AC-E2.5.3); en ese caso incrementa la <see cref="Version"/>.
    /// Editar solo datos no económicos NO emite ni versiona.
    /// </para>
    /// </summary>
    public bool Editar(string tipo, decimal costoBase, decimal impuestos, string ubicacion, int capacidad)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);
        ArgumentException.ThrowIfNullOrWhiteSpace(ubicacion);
        ArgumentOutOfRangeException.ThrowIfNegative(costoBase);
        ArgumentOutOfRangeException.ThrowIfNegative(impuestos);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacidad);

        var cambioPrecio = CostoBase != costoBase || Impuestos != impuestos;

        Tipo = tipo.Trim();
        CostoBase = costoBase;
        Impuestos = impuestos;
        Ubicacion = ubicacion.Trim();
        Capacidad = capacidad; // estado de catálogo; no emite evento propio → no bumpea Version por sí sola

        if (cambioPrecio)
        {
            Version++;
        }

        return cambioPrecio;
    }

    /// <summary>
    /// Habilita la habitación (idempotente). Devuelve <c>true</c> si cambió el estado. NO emite evento
    /// (el contrato de catálogo no tiene «HabitacionHabilitada»; AC-E2.5.3) → NO incrementa la <see cref="Version"/>.
    /// </summary>
    public bool Habilitar()
    {
        if (Estado == EstadoHabitacion.Habilitada)
        {
            return false;
        }

        Estado = EstadoHabitacion.Habilitada;
        return true;
    }

    /// <summary>
    /// Deshabilita la habitación (idempotente): deja de ofertarse. Devuelve <c>true</c> si cambió el estado;
    /// en ese caso emite <c>HabitacionDeshabilitada</c> e incrementa la <see cref="Version"/> (AC-E2.5.3/4).
    /// </summary>
    public bool Deshabilitar()
    {
        if (Estado == EstadoHabitacion.Deshabilitada)
        {
            return false;
        }

        Estado = EstadoHabitacion.Deshabilitada;
        Version++;
        return true;
    }
}
