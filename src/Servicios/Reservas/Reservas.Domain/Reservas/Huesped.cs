namespace Reservas.Domain.Reservas;

/// <summary>
/// Value Object con los datos completos de un huésped de la reserva (FR-10). Todos los campos son
/// obligatorios; el formato/obligatoriedad se validan en el borde (FluentValidation → 400) y la fábrica
/// actúa como defensa en profundidad del invariante de dominio.
/// </summary>
public sealed record Huesped
{
    // Setters privados + ctor sin parámetros para EF Core: el owned anidado (Documento) NO puede bindearse por
    // constructor, así que EF materializa por propiedades. La fábrica Crear sigue siendo la única vía de dominio.
    public string Nombres { get; private set; }
    public string Apellidos { get; private set; }
    public DateOnly FechaNacimiento { get; private set; }
    public string Genero { get; private set; }
    public Documento Documento { get; private set; }
    public string Email { get; private set; }
    public string Telefono { get; private set; }

    private Huesped() // EF Core
    {
        Nombres = string.Empty;
        Apellidos = string.Empty;
        Genero = string.Empty;
        Documento = null!;
        Email = string.Empty;
        Telefono = string.Empty;
    }

    private Huesped(
        string nombres,
        string apellidos,
        DateOnly fechaNacimiento,
        string genero,
        Documento documento,
        string email,
        string telefono)
    {
        Nombres = nombres;
        Apellidos = apellidos;
        FechaNacimiento = fechaNacimiento;
        Genero = genero;
        Documento = documento;
        Email = email;
        Telefono = telefono;
    }

    public static Huesped Crear(
        string nombres,
        string apellidos,
        DateOnly fechaNacimiento,
        string genero,
        Documento documento,
        string email,
        string telefono)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombres);
        ArgumentException.ThrowIfNullOrWhiteSpace(apellidos);
        ArgumentException.ThrowIfNullOrWhiteSpace(genero);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(telefono);
        ArgumentNullException.ThrowIfNull(documento);

        return new Huesped(nombres.Trim(), apellidos.Trim(), fechaNacimiento, genero.Trim(), documento, email.Trim(), telefono.Trim());
    }

    /// <summary>Nombre para mostrar (nombres + apellidos), usado en el evento de confirmación.</summary>
    public string NombreCompleto => $"{Nombres} {Apellidos}";
}
