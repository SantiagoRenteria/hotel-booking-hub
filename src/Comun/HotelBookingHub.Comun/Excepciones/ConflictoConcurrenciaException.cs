using HotelBookingHub.Comun.Resultados;

namespace HotelBookingHub.Comun.Excepciones;

/// <summary>
/// Conflicto de <b>concurrencia optimista</b>: al guardar, el <c>rowversion</c> que traía el cliente ya no
/// coincide con el de la fila (otro editó/eliminó en medio) y el <c>UPDATE ... WHERE RowVersion=@original</c>
/// afectó 0 filas → EF lanza <c>DbUpdateConcurrencyException</c>, que se traduce a esta excepción en el borde
/// de infraestructura. Es un desenlace <b>determinístico</b> (reintentar sin recargar volvería a fallar) → se
/// mapea a <b>409 Conflict</b> y NUNCA se reintenta (a diferencia del deadlock 1205, que sí es transitorio).
/// </summary>
public sealed class ConflictoConcurrenciaException : ExcepcionNegocio
{
    private const string MensajePredeterminado =
        "El recurso fue modificado por otra operación; recargue los datos y reintente.";

    public ConflictoConcurrenciaException(Exception innerException)
        : base(EstadoResultado.Conflicto, MensajePredeterminado, innerException) { }

    public ConflictoConcurrenciaException(string mensaje, Exception innerException)
        : base(EstadoResultado.Conflicto, mensaje, innerException) { }
}
