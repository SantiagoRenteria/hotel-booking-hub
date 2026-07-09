using Reservas.Domain.Reservas;

namespace Reservas.Domain.Servicios;

/// <summary>
/// Domain service PURO (sin I/O) del cálculo de precio de una reserva: <c>(costoBase + impuesto) × noches</c>.
/// 100% unit-testeable — sostiene el TDD del flujo crítico (Story 1.4). Dinero en <see cref="decimal"/>.
/// </summary>
public sealed class CalculadorPrecio
{
    public decimal Calcular(decimal costoBase, decimal impuesto, Estancia estancia)
        => (costoBase + impuesto) * estancia.Noches;
}
