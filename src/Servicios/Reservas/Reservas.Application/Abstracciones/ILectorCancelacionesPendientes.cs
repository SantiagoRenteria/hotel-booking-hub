namespace Reservas.Application.Abstracciones;

/// <summary>
/// Puerto de lectura (CQRS) de las solicitudes de cancelación PENDIENTES de un agente (Story 4.3). El aislamiento
/// por <paramref name="agenteEmail"/> se aplica DENTRO de la consulta. Devuelve la fecha de solicitud (para que el
/// handler derive los "días en espera" con el reloj inyectable) + datos informativos; NO calcula la antigüedad
/// (eso depende del reloj y vive en el handler, determinista para tests).
/// </summary>
public interface ILectorCancelacionesPendientes
{
    Task<IReadOnlyList<CancelacionPendienteFila>> ListarPendientesAsync(string agenteEmail, CancellationToken ct);
}

/// <summary>Fila de lectura de una cancelación pendiente (sin "días en espera": lo deriva el handler con el reloj).</summary>
public sealed record CancelacionPendienteFila(
    Guid ReservaId,
    DateOnly FechaSolicitud,
    string MotivoCategoria,
    decimal PenalidadPorcentaje);
