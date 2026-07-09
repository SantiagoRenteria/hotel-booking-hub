using Reservas.Domain.Servicios;

namespace Reservas.Domain.Reservas;

/// <summary>
/// Aggregate root de una reserva confirmada. Identidad = <b>UUID v7</b> (PK no-clustered; la
/// clustering key <c>Seq</c> es shadow property configurada en infraestructura — ADR-017).
/// Genera sus <see cref="NocheHabitacion"/> (slots de inventario) para toda la estancia; la
/// unicidad de esos slots garantiza el invariante anti-overbooking en el motor.
/// <para>Story 3.3: persiste los datos de negocio que 1.6a solo validaba y llevaba al evento —
/// <see cref="Huespedes"/> (owned collection, FR-10), <see cref="ContactoEmergencia"/> (owned, FR-11),
/// <see cref="AgenteEmail"/> (aislamiento del listado, AC-E3.3.2) y <see cref="PrecioTotal"/> (mostrado tal
/// como se calculó en E1, sin recálculo).</para>
/// </summary>
public sealed class Reserva
{
    private readonly List<NocheHabitacion> _noches = [];
    private readonly List<Huesped> _huespedes = [];

    public Guid Id { get; private set; }
    public Guid HabitacionId { get; private set; }
    public Estancia Estancia { get; private set; } = null!;
    public EstadoReserva Estado { get; private set; }

    /// <summary>Correo del agente que intermedió la reserva; eje de aislamiento del listado (AC-E3.3.2).</summary>
    public string? AgenteEmail { get; private set; }

    /// <summary>Precio total calculado al confirmar (E1). Se muestra tal cual; NO se recalcula en la lectura.</summary>
    public decimal PrecioTotal { get; private set; }

    /// <summary>Contacto de emergencia (FR-11); presente en el detalle (AC-E3.3.1).</summary>
    public ContactoEmergencia? ContactoEmergencia { get; private set; }

    /// <summary>
    /// Episodio de cancelación (owned, NULLABLE): <c>null</c> mientras la reserva sigue solo confirmada;
    /// se puebla al solicitar la cancelación (Story 4.1) y lo completa la resolución (Story 4.2).
    /// </summary>
    public SolicitudCancelacion? SolicitudCancelacion { get; private set; }

    /// <summary>Slots de inventario que ocupa la reserva (una fila por noche del rango).</summary>
    public IReadOnlyCollection<NocheHabitacion> Noches => _noches.AsReadOnly();

    /// <summary>Huéspedes de la reserva (FR-10); presentes en el detalle (AC-E3.3.1).</summary>
    public IReadOnlyCollection<Huesped> Huespedes => _huespedes.AsReadOnly();

    private Reserva() { } // EF Core

    /// <summary>
    /// Crea una reserva confirmada y sus slots para el rango <c>[entrada, salida)</c>. Los datos de negocio
    /// (huéspedes, contacto, agente, precio) son opcionales para no forzar a los tests del invariante
    /// anti-overbooking (que solo necesitan habitación + estancia) a construirlos.
    /// </summary>
    public static Reserva Crear(
        Guid habitacionId,
        Estancia estancia,
        IReadOnlyList<Huesped>? huespedes = null,
        ContactoEmergencia? contactoEmergencia = null,
        string? agenteEmail = null,
        decimal precioTotal = 0m)
    {
        var reserva = new Reserva
        {
            Id = Guid.CreateVersion7(),
            HabitacionId = habitacionId,
            Estancia = estancia,
            Estado = EstadoReserva.Confirmada,
            AgenteEmail = agenteEmail?.Trim().ToLowerInvariant(), // canónico: aislamiento independiente del collation
            PrecioTotal = precioTotal,
            ContactoEmergencia = contactoEmergencia,
        };

        if (huespedes is not null)
        {
            reserva._huespedes.AddRange(huespedes);
        }

        // Slots en orden determinístico ascendente por noche (minimiza deadlocks bajo concurrencia).
        for (var noche = estancia.Entrada; noche < estancia.Salida; noche = noche.AddDays(1))
        {
            reserva._noches.Add(new NocheHabitacion(habitacionId, noche, reserva.Id));
        }

        return reserva;
    }

    /// <summary>
    /// Solicita la cancelación (Story 4.1, AC-E4.1.1/.2/.3). Guards por EXCEPCIÓN de dominio (Task 0):
    /// solo desde <see cref="EstadoReserva.Confirmada"/> (una 2ª solicitud ya no lo está → 409, AC-E4.1.2) y
    /// solo con la estancia NO iniciada (<paramref name="fechaSolicitud"/> &lt; entrada; iniciada → no elegible
    /// → 409, AC-E4.1.3, NO penalidad del 100%). Congela la penalidad calculada en <paramref name="fechaSolicitud"/>
    /// y transiciona a <see cref="EstadoReserva.CancelacionSolicitada"/>. Devuelve la penalidad congelada.
    /// </summary>
    public PenalidadSugerida SolicitarCancelacion(
        MotivoCancelacion motivo,
        IniciadorCancelacion iniciador,
        DateOnly fechaSolicitud,
        CalculadorPenalidad calculadorPenalidad)
    {
        // Guard 1 (AC-E4.1.2): solo desde Confirmada. Una 2ª solicitud ya está en CancelacionSolicitada → 409,
        // y NO se re-congela la penalidad.
        if (Estado != EstadoReserva.Confirmada)
        {
            throw new TransicionEstadoInvalidaException(Estado, EstadoReserva.CancelacionSolicitada);
        }

        // Guard 2 (AC-E4.1.3): estancia NO iniciada (hoy < entrada). Iniciada = no elegible → 409 (no penalidad
        // del 100%). El reloj se resolvió en el handler y llega como fechaSolicitud (dominio determinista).
        if (fechaSolicitud >= Estancia.Entrada)
        {
            throw new TransicionEstadoInvalidaException(
                Estado,
                EstadoReserva.CancelacionSolicitada,
                "No se puede cancelar una reserva cuya estancia ya inició.");
        }

        var penalidad = calculadorPenalidad.Calcular(Estancia, fechaSolicitud);
        SolicitudCancelacion = new SolicitudCancelacion(motivo, iniciador, penalidad, fechaSolicitud);
        Estado = EstadoReserva.CancelacionSolicitada;

        return penalidad;
    }

    /// <summary>
    /// Resuelve la solicitud de cancelación (Story 4.2, AC-E4.2.1/.2/.3). Guard por excepción: solo desde
    /// <see cref="EstadoReserva.CancelacionSolicitada"/> (una 2ª resolución ya no lo está → 409). Aprobar
    /// (aplicando la penalidad congelada o condonándola) → <see cref="EstadoReserva.Cancelada"/> y LIBERA el
    /// inventario (borra las <see cref="Noches"/>, invariante anti-overbooking de E1); rechazar → vuelve a
    /// <see cref="EstadoReserva.Confirmada"/> sin tocar slots. La penalidad NUNCA se recalcula. Registra la
    /// auditoría (quién/cuándo/resultado + default/override) en la MISMA <see cref="SolicitudCancelacion"/>.
    /// </summary>
    public void Resolver(DecisionCancelacion decision, string resueltaPor, DateOnly fechaResolucion, string? motivoRechazo)
    {
        throw new NotImplementedException();
    }
}
