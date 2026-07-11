namespace Reservas.Infrastructure.Idempotencia;

/// <summary>Opciones del almacén de idempotencia de reserva (Story 1.7).</summary>
/// <param name="Ttl">Ventana de retención de una clave de idempotencia (por defecto 24h).</param>
public sealed record OpcionesIdempotenciaReserva(TimeSpan Ttl)
{
    public OpcionesIdempotenciaReserva() : this(TimeSpan.FromHours(24)) { }
}
