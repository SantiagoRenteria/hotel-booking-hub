using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Hoteles.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hoteles.Infrastructure.Outbox;

/// <summary>
/// Procesa un lote del outbox de catálogo (extraído del <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>
/// para poder testearlo directo). Reclama por antigüedad (<c>ReclamadoEn</c> nulo o vencido el lease), persiste el
/// intento ANTES de publicar (no mark-sent prematuro → at-least-once del productor) y marca <c>Enviada</c> SOLO
/// tras publicar con éxito. La no-duplicación se garantiza en el consumidor (E5), no aquí: <c>deliveries &gt;= 1</c>.
/// <para>
/// <b>Head-of-line por agregado</b> (code review 2.5, party-mode Opción A): a diferencia de Reservas —donde cada
/// reserva emite UN solo evento (version≡1), así que no hay orden intra-agregado que proteger— una habitación
/// emite VARIOS eventos ordenados por <c>Version</c>. Si la publicación de uno falla, NO se publica ningún evento
/// POSTERIOR del MISMO agregado en este ciclo (se bloquea su <c>AggregateId</c>); los de otros agregados avanzan.
/// Como los eventos son deltas, adelantar v3 antes de un v2 fallido llevaría a la proyección de E3 a descartar el
/// v2 tardío (v-vieja) y perder el cambio. La garantía de orden cross-instance/cross-lote es AC vinculante del
/// consumidor (E3, Story 3.1, buffer por order key); esto es defensa en profundidad del productor single-instance.
/// </para>
/// </summary>
public sealed class ProcesadorOutbox(IPublicadorEventos publicador, OpcionesRelayOutbox opciones, ILogger<ProcesadorOutbox> logger)
{
    public const string Topico = "hoteles";
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);

    /// <summary>Procesa un lote y devuelve cuántas filas se marcaron <c>Enviada</c> en este ciclo.</summary>
    public async Task<int> ProcesarLoteAsync(HotelesDbContext db, DateTimeOffset ahora, CancellationToken ct)
    {
        var limiteLease = ahora - opciones.Lease;

        var pendientes = await db.OutboxMessages
            .Where(m => m.Estado == OutboxMessage.Pendiente && (m.ReclamadoEn == null || m.ReclamadoEn < limiteLease))
            .OrderBy(m => m.Seq)
            .Take(opciones.TamanoLote)
            .ToListAsync(ct);

        var enviadas = 0;
        var bloqueados = new HashSet<Guid>(); // agregados con un evento anterior fallido en este ciclo
        foreach (var mensaje in pendientes)
        {
            // Un evento anterior del mismo agregado falló: no adelantar este (preserva el orden por Version).
            // No se reclama siquiera → conserva su lease para reintentarse en orden en un ciclo posterior.
            if (bloqueados.Contains(mensaje.AggregateId))
            {
                continue;
            }

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
                // Bloquea el agregado: sus eventos posteriores no se adelantan en este ciclo (head-of-line).
                bloqueados.Add(mensaje.AggregateId);
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
