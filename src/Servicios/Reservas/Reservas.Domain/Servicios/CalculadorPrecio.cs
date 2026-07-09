using Reservas.Domain.Reservas;

namespace Reservas.Domain.Servicios;

/// <summary>
/// Domain service PURO (sin I/O) del cálculo de precio de una reserva: <c>(costoBase + impuesto) × noches</c>.
/// 100% unit-testeable — sostiene el TDD del flujo crítico (Story 1.4). Dinero en <see cref="decimal"/>.
/// </summary>
public sealed class CalculadorPrecio
{
    public decimal Calcular(decimal costoBase, decimal impuesto, Estancia estancia)
    {
        // El dinero nunca es negativo: costoBase/impuesto provienen del catálogo, no del usuario;
        // un valor negativo es una violación de invariante, no un flujo de negocio esperado.
        ArgumentOutOfRangeException.ThrowIfNegative(costoBase);
        ArgumentOutOfRangeException.ThrowIfNegative(impuesto);

        return (costoBase + impuesto) * estancia.Noches;
    }
}
