using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Reservas.Infrastructure.Persistencia;

namespace Reservas.Infrastructure.Outbox;

/// <summary>
/// Procesa un lote del outbox (extraído del <see cref="BackgroundService"/> para poder testearlo directo).
/// Reclama por antigüedad (<c>ReclamadoEn</c> nulo o vencido el lease), persiste el intento ANTES de publicar
/// (no mark-sent prematuro → at-least-once del productor) y marca <c>Enviada</c> SOLO tras publicar con éxito.
/// La no-duplicación se garantiza en el consumidor (E5), no aquí: <c>deliveries &gt;= 1</c>, nunca <c>== 1</c>.
/// </summary>
public sealed class ProcesadorOutbox(IPublicadorEventos publicador, OpcionesRelayOutbox opciones, ILogger<ProcesadorOutbox> logger)
{
    public const string Topico = "reservas";
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);

    /// <summary>Procesa un lote y devuelve cuántas filas se marcaron <c>Enviada</c> en este ciclo.</summary>
    public async Task<int> ProcesarLoteAsync(ReservasDbContext db, DateTimeOffset ahora, CancellationToken ct)
    {
        var limiteLease = ahora - opciones.Lease;

        var pendientes = await db.OutboxMessages
            .Where(m => m.Estado == OutboxMessage.Pendiente && (m.ReclamadoEn == null || m.ReclamadoEn < limiteLease))
            .OrderBy(m => m.Seq)
            .Take(opciones.TamanoLote)
            .ToListAsync(ct);

        var enviadas = 0;
        foreach (var mensaje in pendientes)
        {
            mensaje.Reclamar(ahora);
            await db.SaveChangesAsync(ct); // el intento se persiste ANTES de publicar

            try
            {
                var evento = JsonSerializer.Deserialize<EventoIntegracion>(mensaje.Payload, _opciones)!;
                await publicador.PublicarAsync(Topico, evento, ct);
            }
            catch (Exception ex)
            {
                // No se marca Enviada: la fila sigue Pendiente y se re-reclamará al vencer el lease (at-least-once).
                logger.LogWarning(ex, "Fallo al publicar el mensaje de outbox {MessageId}; se reintentará", mensaje.MessageId);
                continue;
            }

            mensaje.MarcarEnviada();
            await db.SaveChangesAsync(ct);
            enviadas++;
        }

        return enviadas;
    }
}
