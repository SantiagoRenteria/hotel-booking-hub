using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Observabilidad;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>Opciones del <see cref="DespachadorNotificaciones"/>.</summary>
/// <param name="MaxIntentos">Tope de intentos antes de mandar el mensaje a dead-letter (debe ser ≥ 1).</param>
public sealed record OpcionesDespachador(int MaxIntentos = 5)
{
    // Un tope < 1 degrada la garantía en silencio (dead-letter al primer fallo si es 0; reintento infinito que
    // bloquea el stream si es negativo). Se rechaza en construcción para fallar temprano, no en runtime (F3).
    public int MaxIntentos { get; } =
        MaxIntentos >= 1
            ? MaxIntentos
            : throw new ArgumentOutOfRangeException(nameof(MaxIntentos), MaxIntentos, "MaxIntentos debe ser ≥ 1.");
}

/// <summary>
/// Envuelve el procesamiento de un evento (Story 5.1b Task 4) con <b>tope de intentos + dead-letter</b>: un
/// mensaje-veneno (que falla SIEMPRE) no debe re-reclamarse sin cota ni bloquear el stream. Complementa la
/// idempotencia del consumidor (que evita duplicados); aquí se acota el fallo permanente.
/// </summary>
public sealed class DespachadorNotificaciones(
    IProcesadorEvento procesador,
    IContadorReintentos contador,
    IColaDeadLetter deadLetter,
    OpcionesDespachador opciones)
{
    public async Task DespacharAsync(EventoIntegracion evento, CancellationToken ct)
    {
        // Story 7.1 (AC-E7.1.3): span de consumidor correlacionado con la traza originante por el trace-id de
        // negocio del envelope, para que la actividad del worker sea atribuible a la petición que originó el evento.
        using var actividad = ActividadHotelBookingHub.IniciarConsumo($"consumir {evento.Type}", evento.TraceId);
        try
        {
            await procesador.ProcesarAsync(evento, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            actividad?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            actividad?.AddException(ex); // Graba tipo + mensaje en el span (AC-E7.1.4) también en el salto asíncrono.
            var intentos = await contador.IncrementarAsync(evento.Id, ct);
            if (intentos >= opciones.MaxIntentos)
            {
                // Tope agotado → apartar a dead-letter y hacer ACK (no relanzar): el veneno no re-reclama ni
                // bloquea el stream. El contador se reinicia para no arrastrar el conteo a un id reutilizado.
                await deadLetter.EnviarAsync(evento, ex.Message, (int)intentos, ct);
                await contador.ReiniciarAsync(evento.Id, ct);
                return;
            }

            throw; // Aún hay presupuesto de intentos → propagar para que el transporte re-entregue (at-least-once).
        }

        // Éxito: el mensaje dejó de ser candidato a veneno; se limpia su conteo de intentos.
        await contador.ReiniciarAsync(evento.Id, ct);
    }
}
