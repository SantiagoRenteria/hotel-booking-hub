namespace HotelBookingHub.Comun.Eventos;

/// <summary>
/// Carga útil del evento de catálogo <c>HabitacionAgregada.v1</c> que produce el BC de Hoteles cuando se da de
/// alta una habitación (Story 2.5). Lo consumirá la proyección de disponibilidad de E3.
///
/// Contrato congelado (contract test AC-E2.5.1):
/// - <b>Order key</b> = (<see cref="AggregateId"/> = HabitacionId, <see cref="EventoIntegracion.Version"/>) → E3.
/// - <see cref="EventoIntegracion.Type"/> = <see cref="Tipo"/> (PascalCase + semver <c>.v1</c>).
///
/// Formato (System.Text.Json): camelCase; dinero en <see cref="decimal"/>; enums como string. No expone <c>Seq</c>.
///
/// <para><see cref="Ciudad"/> y <see cref="Capacidad"/> se DENORMALIZAN aquí (Épica 3, party-mode A2): la
/// ciudad pertenece al <c>Hotel</c> y la proyección de E3 la necesita para la búsqueda (E3.2), pero Hoteles no
/// emite eventos de Hotel; se captura el valor vigente al momento del alta. Cambio de campos = aditivo sobre
/// <c>.v1</c> (E3 es el primer consumidor; no hay ninguno desplegado que romper).</para>
/// </summary>
public sealed record HabitacionAgregadaV1(
    Guid AggregateId,        // = HabitacionId (UUID v7); componente de la order key.
    Guid HotelId,
    string TipoHabitacion,   // clase de la habitación (Suite, etc.); nombre distinto del Tipo del evento.
    decimal CostoBase,
    decimal Impuestos,
    string Ubicacion,
    string Estado,
    string Ciudad,           // denormalizada del hotel al momento del alta (la proyección de E3 filtra por ella).
    int Capacidad)           // nº de huéspedes; E3.2 filtra por capacidad >= huéspedes.
{
    /// <summary>Tipo del evento (PascalCase español + semver). Va en <see cref="EventoIntegracion.Type"/>.</summary>
    public const string Tipo = "HabitacionAgregada.v1";
}
