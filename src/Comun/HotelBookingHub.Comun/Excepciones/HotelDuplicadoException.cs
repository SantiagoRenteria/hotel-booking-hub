using HotelBookingHub.Comun.Resultados;

namespace HotelBookingHub.Comun.Excepciones;

/// <summary>
/// Duplicado de catálogo arbitrado por el motor: ya existe un hotel <b>no eliminado</b> del mismo agente con el
/// mismo <c>(Nombre, Ciudad)</c>. El árbitro es un índice único filtrado <c>UNIQUE(AgentePropietario, Nombre,
/// Ciudad) WHERE Eliminado = 0</c> (mismo patrón que el anti-overbooking, ADR-016: la unicidad se decide en el
/// INSERT/UPDATE, no con un pre-check con carrera). La violación (SQL 2601/2627) emerge dentro de
/// <c>SaveChanges</c> y se traduce a esta excepción en el borde de infraestructura → <b>409 Conflict</b>. Cubre
/// tanto el alta como la edición (renombrar un hotel a un par ya usado). Acotado <b>por agente</b>: dos agentes
/// distintos pueden tener el mismo nombre+ciudad en sus catálogos (sin interferencia ni fuga de datos ajenos).
/// </summary>
public sealed class HotelDuplicadoException : ExcepcionNegocio
{
    private const string MensajePredeterminado =
        "Ya existe un hotel con ese nombre en esa ciudad.";

    public HotelDuplicadoException(Exception innerException)
        : base(EstadoResultado.Conflicto, MensajePredeterminado, innerException) { }
}
