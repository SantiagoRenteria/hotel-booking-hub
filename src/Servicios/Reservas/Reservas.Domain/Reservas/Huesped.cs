namespace Reservas.Domain.Reservas;

/// <summary>
/// Value Object con los datos completos de un huésped de la reserva (FR-10). Todos los campos son
/// obligatorios; el formato/obligatoriedad se validan en el borde (FluentValidation → 400) y la fábrica
/// actúa como defensa en profundidad del invariante de dominio.
/// </summary>
public sealed record Huesped
{
    public string Nombres { get; }
    public string Apellidos { get; }
    public DateOnly FechaNacimiento { get; }
    public string Genero { get; }
    public Documento Documento { get; }
    public string Email { get; }
    public string Telefono { get; }

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
