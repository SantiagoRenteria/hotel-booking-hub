using HotelBookingHub.Comun.Eventos;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>Opciones del <see cref="DespachadorNotificaciones"/>.</summary>
/// <param name="MaxIntentos">Tope de intentos antes de mandar el mensaje a dead-letter (debe ser ≥ 1).</param>
public sealed record OpcionesDespachador(int MaxIntentos = 5);

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
        _ = (contador, deadLetter, opciones); // TODO 5.1b GREEN: tope de intentos → dead-letter.
        await procesador.ProcesarAsync(evento, ct);
    }
}
