namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Inbox de idempotencia del CONSUMIDOR (Story 5.1b, AC-E5.1b.1). Reutiliza el PATRÓN de inbox decidido en
/// <c>AC-E3.1.4</c> (party-mode 3.1 D3), pero con el store adecuado al efecto: el envío de correo es un efecto
/// externo NO transaccionable con SMTP, así que aquí la dedup es <b>Redis <c>SETNX</c> + TTL</b> (best-effort
/// at-least-once), no una tabla SQL como en E3 (donde el efecto —upsert— sí entra en la misma transacción).
///
/// <para><b>Orden marcado/efecto (decisión de diseño, Task 1).</b> Se usa <b>reservar-antes-de-enviar</b>
/// (<c>SETNX</c> reserva la clave del efecto) y <b>liberar-si-falla</b>:</para>
/// <list type="number">
///   <item><see cref="IntentarMarcarProcesadoAsync"/> hace <c>SETNX</c>: si gana la reserva → procede el efecto;
///     si ya estaba reservada → el efecto ya se realizó (o está en curso) y se descarta (dedup → sin duplicado).</item>
///   <item>Si el efecto se realiza con éxito, la clave permanece hasta expirar por TTL (mantiene la dedup).</item>
///   <item>Si el efecto FALLA, <see cref="LiberarAsync"/> borra la reserva para que la re-entrega
///     (at-least-once) lo reintente SIN duplicar (sin pérdida).</item>
/// </list>
/// <para><b>Límites explícitos.</b> No hay exactamente-una-vez dura con un efecto externo no transaccional:
/// (a) si el proceso CAE entre la reserva y el envío, la clave queda puesta y el mensaje no se re-enviaría hasta
/// que el TTL expire (ventana at-most-once acotada por el TTL, que se autocorrige por re-entrega tras expirar);
/// (b) el TTL debe ser MAYOR que la ventana máxima de re-entrega del broker, o una re-entrega tardía tras expirar
/// re-enviaría (duplicado). El efecto se modela <b>por destinatario</b> (parámetro <paramref name="efecto"/>): así
/// un fallo parcial (1er correo enviado, 2º falla) reintenta solo el pendiente, sin duplicar el que ya salió.</para>
/// </summary>
public interface IInboxIdempotencia
{
    /// <summary>
    /// Reserva atómica (<c>SETNX</c> + TTL) de la clave <c>(messageId, version, efecto)</c>.
    /// </summary>
    /// <param name="efecto">Discriminador del efecto concreto dentro del mensaje (p. ej. destinatario:
    ///   <c>"huesped"</c>/<c>"agente"</c>): un mensaje produce varios efectos y cada uno se deduplica por separado.</param>
    /// <returns><c>true</c> si esta invocación ganó la reserva (procede el efecto); <c>false</c> si ya estaba
    ///   reservada (descartar el efecto: duplicado).</returns>
    Task<bool> IntentarMarcarProcesadoAsync(Guid messageId, int version, string efecto, CancellationToken ct);

    /// <summary>
    /// Libera la reserva de <c>(messageId, version, efecto)</c> (borra la clave) para permitir el reintento sin
    /// duplicar, cuando el efecto reservado terminó fallando.
    /// </summary>
    Task LiberarAsync(Guid messageId, int version, string efecto, CancellationToken ct);
}
