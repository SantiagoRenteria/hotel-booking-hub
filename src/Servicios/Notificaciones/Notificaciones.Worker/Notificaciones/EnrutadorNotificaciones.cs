using HotelBookingHub.Comun.Eventos;

namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Enruta cada evento entrante a su <see cref="IProcesadorEvento"/> por <c>evento.Type</c> (Story 9.1, resuelve el
/// TODO "el enrutamiento por tipo llega con el transporte"). Cada tipo se despacha por su propio
/// <see cref="DespachadorNotificaciones"/> (tope de intentos + dead-letter compartidos), de modo que la semántica
/// de reentrega/veneno de 5.1b se conserva. Un tipo no mapeado se ignora (no es error: otros BCs publican al
/// mismo transporte). Relanza si el despachador aún tiene presupuesto de intentos (para reentrega del broker).
/// </summary>
public sealed class EnrutadorNotificaciones
{
    private readonly Dictionary<string, DespachadorNotificaciones> _porTipo;

    public EnrutadorNotificaciones(
        ConsumidorReservaConfirmada confirmacion,
        ConsumidorSolicitudCancelacion solicitud,
        ConsumidorResolucionCancelacion resolucion,
        IContadorReintentos contador,
        IColaDeadLetter deadLetter,
        OpcionesDespachador opciones)
    {
        DespachadorNotificaciones Despachador(IProcesadorEvento procesador) =>
            new(procesador, contador, deadLetter, opciones);

        _porTipo = new Dictionary<string, DespachadorNotificaciones>(StringComparer.Ordinal)
        {
            [ReservaConfirmadaV1.Tipo] = Despachador(confirmacion),
            [SolicitudCancelacionRegistradaV1.Tipo] = Despachador(solicitud),
            [ReservaCanceladaV1.Tipo] = Despachador(resolucion),
            [SolicitudCancelacionRechazadaV1.Tipo] = Despachador(resolucion),
        };
    }

    /// <summary>
    /// Despacha el evento al procesador de su tipo. Devuelve <c>true</c> si el mensaje quedó resuelto
    /// (procesado o apartado a dead-letter → ACK); propaga la excepción si aún hay presupuesto de reintentos
    /// (→ NACK/requeue para reentrega). Un tipo desconocido devuelve <c>true</c> (ACK, nada que hacer).
    /// </summary>
    public async Task<bool> EnrutarAsync(EventoIntegracion evento, CancellationToken ct)
    {
        if (!_porTipo.TryGetValue(evento.Type, out var despachador))
        {
            return true;
        }

        await despachador.DespacharAsync(evento, ct);
        return true;
    }
}
