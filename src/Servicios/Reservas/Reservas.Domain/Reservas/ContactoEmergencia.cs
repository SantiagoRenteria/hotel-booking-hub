namespace Reservas.Domain.Reservas;

/// <summary>
/// Value Object del contacto de emergencia de la reserva (FR-11): nombre completo + teléfono, obligatorio.
/// </summary>
public sealed record ContactoEmergencia
{
    public string NombreCompleto { get; }
    public string Telefono { get; }

    private ContactoEmergencia(string nombreCompleto, string telefono)
    {
        NombreCompleto = nombreCompleto;
        Telefono = telefono;
    }

    public static ContactoEmergencia Crear(string nombreCompleto, string telefono)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombreCompleto);
        ArgumentException.ThrowIfNullOrWhiteSpace(telefono);
        return new ContactoEmergencia(nombreCompleto.Trim(), telefono.Trim());
    }
}
