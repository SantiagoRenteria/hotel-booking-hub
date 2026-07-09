namespace Reservas.Domain.Reservas;

/// <summary>
/// Se lanza cuando se intenta construir una <see cref="Estancia"/> con un rango inválido
/// (la fecha de salida no es posterior a la de entrada). Invariante de dominio.
/// </summary>
public sealed class EstanciaInvalidaException : Exception
{
    public EstanciaInvalidaException() { }
    public EstanciaInvalidaException(string message) : base(message) { }
    public EstanciaInvalidaException(string message, Exception innerException) : base(message, innerException) { }
}
