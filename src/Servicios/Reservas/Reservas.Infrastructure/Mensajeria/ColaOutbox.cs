using System.Security.Cryptography;
using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Reservas.Application.Abstracciones;
using Reservas.Infrastructure.Persistencia;

namespace Reservas.Infrastructure.Mensajeria;

/// <summary>
/// Implementación EF Core de <see cref="IColaOutbox"/>: STAGEA la fila de outbox (serializa el envelope como
/// payload) en el mismo <c>DbContext</c> del cambio de dominio. El <c>MessageId</c> viene del
/// <see cref="ContextoMensajeria"/> (asignado una vez por el <c>TransactionBehavior</c>).
/// </summary>
public sealed class ColaOutbox(ReservasDbContext db, ContextoMensajeria contexto) : IColaOutbox
{
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);

    public void Encolar(string tipo, int version, Guid aggregateId, object data, string? traceId)
    {
        // MessageId POR-MENSAJE (Story 4.3, Task 0): un comando puede emitir N eventos (p. ej. el atajo de
        // cancelación emite solicitud + resolución). El ordinal 0 usa el MessageId del comando —CERO cambio
        // observable para los comandos mono-evento (1.3/1.6b/2.5/4.1/4.2)— y los siguientes derivan un id
        // determinista (UUIDv5 sobre semilla+ordinal). Es estable ante el retry 1205: la semilla se fija una
        // vez y ReiniciarConteo() reinicia el ordinal por-intento → misma secuencia → mismos ids → la
        // UNIQUE(MessageId) deduplica el reintento. Una colisión REAL de MessageId (bug) la arbitra el motor:
        // el EjecutorTransaccional la clasifica por entidad ofensora → NO negocio (500), nunca 409.
        var ordinal = contexto.RegistrarEventoEncolado() - 1;
        var messageId = ordinal == 0 ? contexto.MessageId : DerivarMessageId(contexto.MessageId, ordinal);

        var ahora = DateTimeOffset.UtcNow;

        var envelope = new EventoIntegracion(messageId, tipo, version, ahora, traceId, data);
        var payload = JsonSerializer.Serialize(envelope, _opciones);

        db.OutboxMessages.Add(OutboxMessage.Crear(messageId, aggregateId, tipo, payload, ahora));
    }

    /// <summary>
    /// Deriva un <c>MessageId</c> determinista de (semilla del comando, ordinal del evento). MD5 sobre
    /// semilla(16)+ordinal(4) → 16 bytes: NO es criptográfico (es un id interno de dedup, no un secreto),
    /// solo necesita ser puro/estable entre runtimes y libre de colisiones para semillas distintas.
    /// </summary>
    private static Guid DerivarMessageId(Guid semilla, int ordinal)
    {
        Span<byte> entrada = stackalloc byte[20];
        semilla.TryWriteBytes(entrada[..16]);
        BitConverter.TryWriteBytes(entrada[16..], ordinal);

        Span<byte> hash = stackalloc byte[16];
        MD5.HashData(entrada, hash);
        return new Guid(hash);
    }
}
