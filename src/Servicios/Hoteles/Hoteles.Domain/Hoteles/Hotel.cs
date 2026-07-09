namespace Hoteles.Domain.Hoteles;

/// <summary>
/// Aggregate root del catálogo del agente. Identidad <b>UUID v7</b> (PK no-clustered; la clustering key
/// <c>Seq</c> es shadow property en infraestructura — ADR-017). Setters privados; se crea por
/// <see cref="Crear"/> y muta por <see cref="Editar"/> / <see cref="Eliminar"/>, que garantizan los invariantes
/// obligatorios (nombre y ciudad no vacíos). La baja es <b>lógica</b> (<see cref="Eliminado"/>): no hay borrado
/// físico; el <c>HotelesDbContext</c> filtra los eliminados con un query filter global.
/// </summary>
public sealed class Hotel
{
    public Guid Id { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public string Ciudad { get; private set; } = string.Empty;
    public string Direccion { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public EstadoHotel Estado { get; private set; }

    /// <summary>Baja lógica (soft delete): un hotel eliminado deja de aparecer en consultas y de ofertar.</summary>
    public bool Eliminado { get; private set; }

    private Hotel() { } // EF Core

    public static Hotel Crear(string nombre, string ciudad, string direccion, string descripcion, EstadoHotel estado)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentException.ThrowIfNullOrWhiteSpace(ciudad);

        return new Hotel
        {
            Id = Guid.CreateVersion7(),
            Nombre = nombre.Trim(),
            Ciudad = ciudad.Trim(),
            Direccion = (direccion ?? string.Empty).Trim(),
            Descripcion = (descripcion ?? string.Empty).Trim(),
            Estado = estado,
        };
    }

    /// <summary>
    /// Actualiza los datos descriptivos del hotel (AC-E2.2.1). NO cambia el <see cref="Estado"/>: la transición
    /// del ciclo de vida (habilitar/deshabilitar) es una operación dedicada con sus propias reglas (Story 2.3),
    /// no un set silencioso del CRUD. Tampoco toca sus habitaciones (independencia).
    /// </summary>
    public void Editar(string nombre, string ciudad, string direccion, string descripcion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentException.ThrowIfNullOrWhiteSpace(ciudad);

        Nombre = nombre.Trim();
        Ciudad = ciudad.Trim();
        Direccion = (direccion ?? string.Empty).Trim();
        Descripcion = (descripcion ?? string.Empty).Trim();
    }

    /// <summary>Da de baja lógica el hotel (AC-E2.2.2): marca <see cref="Eliminado"/> sin borrado físico.</summary>
    public void Eliminar() => Eliminado = true;

    /// <summary>
    /// Habilita el hotel (AC-E2.3.1). Idempotente. Junto con <see cref="Deshabilitar"/> es la ÚNICA vía de
    /// transición del ciclo de vida: la edición (2.2) no toca el estado (un solo dueño de la regla).
    /// </summary>
    public void Habilitar() => Estado = EstadoHotel.Habilitado;

    /// <summary>Deshabilita el hotel (AC-E2.3.1): deja de ofertarse. Idempotente. Ver <see cref="Habilitar"/>.</summary>
    public void Deshabilitar() => Estado = EstadoHotel.Deshabilitado;
}
