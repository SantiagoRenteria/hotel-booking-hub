namespace Reservas.Infrastructure.Outbox;

/// <summary>Parámetros del relay de outbox. Valores por defecto razonables para dev; ajustables por servicio.</summary>
public sealed class OpcionesRelayOutbox
{
    /// <summary>Cada cuánto sondea la tabla en busca de filas pendientes.</summary>
    public TimeSpan Intervalo { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Antigüedad del reclamo tras la cual una fila pendiente se re-reclama (evita huérfanos).</summary>
    public TimeSpan Lease { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Máximo de filas por ciclo.</summary>
    public int TamanoLote { get; init; } = 100;
}
