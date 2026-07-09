namespace Reservas.Domain.Reservas;

/// <summary>
/// Value Object del documento de identidad de un huésped: tipo (p. ej. "CC", "Pasaporte") + número.
/// El formato se valida en el borde (FluentValidation → 400); la fábrica es defensa en profundidad.
/// </summary>
public sealed record Documento
{
    public string Tipo { get; }
    public string Numero { get; }

    private Documento(string tipo, string numero)
    {
        Tipo = tipo;
        Numero = numero;
    }

    public static Documento Crear(string tipo, string numero)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);
        ArgumentException.ThrowIfNullOrWhiteSpace(numero);
        return new Documento(tipo.Trim(), numero.Trim());
    }
}
