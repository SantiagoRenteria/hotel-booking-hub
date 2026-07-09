using HotelBookingHub.Comun.Resultados;

namespace HotelBookingHub.Comun.Excepciones;

/// <summary>
/// Base de los fallos de negocio que NO pueden expresarse como <see cref="Result"/> porque emergen dentro
/// de <c>SaveChanges</c> (en infraestructura, cuando el handler ya devolvió): overbooking arbitrado por el
/// motor (Reservas), conflicto de concurrencia optimista (Hoteles), etc.
/// <para>
/// Transporta un <see cref="EstadoResultado"/> —la <b>semántica de negocio</b>, no el detalle HTTP— para que
/// el handler transversal (<c>Comun.Web</c>) la traduzca con la MISMA tabla <c>EstadoResultado→HTTP</c> que ya
/// usa el mapeo de <see cref="Result"/>: una sola fuente de verdad para el status en todo el sistema
/// (decisión de diseño Story 2.2 — Alternativa C). <c>abstract</c>: nadie lanza una de estas «pelada», cada
/// caso concreto declara su estado.
/// </para>
/// </summary>
public abstract class ExcepcionNegocio : Exception
{
    /// <summary>Semántica del fallo (p. ej. <see cref="EstadoResultado.Conflicto"/> → 409). No es HTTP.</summary>
    public EstadoResultado Estado { get; }

    protected ExcepcionNegocio(EstadoResultado estado, string mensaje, Exception? innerException = null)
        : base(mensaje, innerException) => Estado = estado;
}
