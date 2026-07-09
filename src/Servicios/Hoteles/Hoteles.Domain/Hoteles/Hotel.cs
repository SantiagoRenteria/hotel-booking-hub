namespace Hoteles.Domain.Hoteles;

/// <summary>
/// Aggregate root del catálogo del agente. Identidad <b>UUID v7</b> (PK no-clustered; la clustering key
/// <c>Seq</c> es shadow property en infraestructura — ADR-017). Setters privados; se crea por
/// <see cref="Crear"/>, que garantiza los invariantes obligatorios (nombre y ciudad no vacíos).
/// </summary>
public sealed class Hotel
{
    public Guid Id { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public string Ciudad { get; private set; } = string.Empty;
    public string Direccion { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public EstadoHotel Estado { get; private set; }

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
}
