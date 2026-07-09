using Reservas.Domain.Reservas;

namespace Reservas.Domain.Servicios;

/// <summary>
/// Domain service PURO (sin I/O, sin reloj) de la penalidad de cancelación SUGERIDA (Story 4.1), familia de
/// <see cref="CalculadorPrecio"/>. Regla: <c>&gt;=30</c> días a la entrada → <c>0%</c>; <c>&lt;30</c> → <c>100%</c>.
/// La fecha de solicitud se resuelve en el HANDLER (vía <c>TimeProvider</c>) y se pasa como <see cref="DateOnly"/>,
/// de modo que el cálculo es determinista y 100% unit-testeable (decisión party-mode Task 0).
/// </summary>
public sealed class CalculadorPenalidad
{
    /// <summary>Días de antelación a la entrada a partir de los cuales la cancelación no tiene penalidad.</summary>
    public const int DiasUmbralSinPenalidad = 30;

    public PenalidadSugerida Calcular(Estancia estancia, DateOnly fechaSolicitud)
    {
        var diasDeAntelacion = estancia.Entrada.DayNumber - fechaSolicitud.DayNumber;

        return diasDeAntelacion >= DiasUmbralSinPenalidad
            ? PenalidadSugerida.Crear(0m)
            : PenalidadSugerida.Crear(100m);
    }
}
