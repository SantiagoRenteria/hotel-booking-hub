namespace Reservas.Infrastructure.Persistencia;

/// <summary>
/// Clasifica errores de SQL Server por <c>SqlException.Number</c> (NUNCA por el mensaje — anti-patrón).
/// Fuente: spike 1.2 / ADR-016.
/// </summary>
public static class ClasificacionSqlServer
{
    public const int ViolacionPkUnica = 2627;    // PRIMARY KEY / UNIQUE constraint
    public const int ViolacionIndiceUnico = 2601; // unique index
    public const int Deadlock = 1205;             // deadlock victim

    /// <summary>Violación de único → el otro ganó la carrera → 409 sin retry (determinístico).</summary>
    public static bool EsViolacionDeUnico(int number) => number is ViolacionPkUnica or ViolacionIndiceUnico;

    /// <summary>Deadlock → reintentable (backoff + jitter).</summary>
    public static bool EsDeadlock(int number) => number == Deadlock;
}
