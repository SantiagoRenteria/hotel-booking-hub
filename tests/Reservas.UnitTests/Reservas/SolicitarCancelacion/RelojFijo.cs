namespace Reservas.UnitTests.SolicitarCancelacion;

/// <summary>
/// <see cref="TimeProvider"/> fijo para tests deterministas: devuelve siempre la fecha configurada. Permite
/// probar la CONGELACIÓN de la penalidad por fecha (Story 4.1) sin depender del reloj del sistema.
/// </summary>
public sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => ahora;

    public static RelojFijo EnFecha(DateOnly fecha) =>
        new(new DateTimeOffset(fecha.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
}
