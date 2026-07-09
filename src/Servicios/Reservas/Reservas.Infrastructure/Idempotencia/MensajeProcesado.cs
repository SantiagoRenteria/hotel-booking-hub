namespace Reservas.Infrastructure.Idempotencia;

/// <summary>
/// Fila del inbox de idempotencia (Story 3.1, AC-E3.1.4): registra el <c>MessageId</c> de un evento ya
/// procesado. Se inserta en la MISMA transacción que el upsert de la proyección, así que "marcar procesado" y
/// "aplicar" son atómicos (evita el dual-write que perdería el evento si Redis marca pero SQL falla). La
/// no-duplicación dura la da la PK; Redis <c>SETNX+TTL</c> queda como fast-path opcional. Patrón reutilizable por
/// E5 (allí el efecto es externo —email— y usará SETNX, no esta tabla).
/// </summary>
public sealed class MensajeProcesado
{
    public Guid MessageId { get; private set; }
    public DateTimeOffset ProcesadoEn { get; private set; }

    private MensajeProcesado() { } // EF Core

    public static MensajeProcesado Crear(Guid messageId, DateTimeOffset procesadoEn) =>
        new() { MessageId = messageId, ProcesadoEn = procesadoEn };
}
