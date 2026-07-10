using Microsoft.EntityFrameworkCore;
using Reservas.Application.Abstracciones;
using Reservas.Domain.Reservas;
using Reservas.Infrastructure.Persistencia;

namespace Reservas.Infrastructure.Proyeccion;

/// <summary>
/// Lectura de las solicitudes de cancelación PENDIENTES del agente (Story 4.3, AC-E4.3.3). Aislamiento
/// server-side: filtra por <c>AgenteEmail</c> y por estado <c>CancelacionSolicitada</c>. Devuelve la fecha de
/// solicitud (la owned <c>SolicitudCancelacion</c> vive en la propia tabla) ordenada por antigüedad; los "días en
/// espera" los deriva el handler con el reloj inyectable.
/// </summary>
public sealed class LectorCancelacionesPendientesSql(ReservasDbContext db) : ILectorCancelacionesPendientes
{
    public async Task<IReadOnlyList<CancelacionPendienteFila>> ListarPendientesAsync(string agenteEmail, CancellationToken ct) =>
        await db.Reservas.AsNoTracking()
            .Where(r => r.AgenteEmail == agenteEmail && r.Estado == EstadoReserva.CancelacionSolicitada)
            .OrderBy(r => r.SolicitudCancelacion!.FechaSolicitud).ThenBy(r => r.Id)
            .Select(r => new CancelacionPendienteFila(
                r.Id,
                r.SolicitudCancelacion!.FechaSolicitud,
                r.SolicitudCancelacion.MotivoCategoria,
                r.SolicitudCancelacion.PenalidadPorcentaje))
            .ToListAsync(ct);
}
