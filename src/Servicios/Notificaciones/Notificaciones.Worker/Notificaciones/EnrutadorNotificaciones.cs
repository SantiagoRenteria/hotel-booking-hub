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
    /// Despacha el evento al procesador de su tipo. Retorna normalmente si el mensaje quedó resuelto (procesado o
    /// apartado a dead-letter → el consumidor hace ACK); PROPAGA la excepción si aún hay presupuesto de reintentos
    /// (→ el consumidor hace NACK/requeue para reentrega). Un <c>Type</c> nulo/vacío o desconocido retorna sin
    /// hacer nada (ACK): otros BCs publican al mismo transporte y un envelope sin tipo enrutable no es reintentable.
    /// </summary>
    public async Task EnrutarAsync(EventoIntegracion evento, CancellationToken ct)
    {
        // Guard de Type nulo/vacío ANTES del lookup: Dictionary.TryGetValue(null) lanza ArgumentNullException, que
        // ocurriría fuera de todo DespachadorNotificaciones → sin tope de intentos → requeue infinito. Se trata
        // como tipo no enrutable (ACK), igual que un tipo desconocido.
        if (string.IsNullOrEmpty(evento.Type) || !_porTipo.TryGetValue(evento.Type, out var despachador))
        {
            return;
        }

        await despachador.DespacharAsync(evento, ct);
    }
}
