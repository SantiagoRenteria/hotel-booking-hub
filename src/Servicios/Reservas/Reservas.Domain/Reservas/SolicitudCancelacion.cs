namespace Reservas.Domain.Reservas;

/// <summary>
/// Episodio de cancelación de una <see cref="Reserva"/> (owned, NULLABLE). Story 4.1 registra la SOLICITUD
/// (motivo, iniciador, penalidad congelada, fecha). Story 4.2 completará ESTE MISMO objeto con la resolución
/// (un solo episodio, no dos owned — decisión party-mode Task 0). La penalidad se congela aquí y no se recalcula.
/// </summary>
public sealed class SolicitudCancelacion
{
    public string MotivoCategoria { get; private set; } = null!;
    public string MotivoDetalle { get; private set; } = null!;
    public IniciadorCancelacion IniciadaPor { get; private set; }

    /// <summary>Porcentaje de penalidad CONGELADO en la fecha de solicitud (no se recalcula en 4.2).</summary>
    public decimal PenalidadPorcentaje { get; private set; }

    public DateOnly FechaSolicitud { get; private set; }

    // ---- Resolución (Story 4.2): mismo episodio, campos NULOS hasta que el agente resuelve. ----

    /// <summary>Correo del agente que resolvió; null mientras está pendiente.</summary>
    public string? ResueltaPor { get; private set; }

    public DateOnly? FechaResolucion { get; private set; }

    public ResultadoResolucion? Resultado { get; private set; }

    /// <summary>Penalidad efectivamente aplicada al APROBAR (solo si <see cref="Resultado"/> == Aprobada).</summary>
    public decimal? PenalidadAplicadaPorcentaje { get; private set; }

    /// <summary><c>true</c> si el agente sobrescribió la penalidad sugerida congelada (p. ej. condonó).</summary>
    public bool PenalidadFueOverride { get; private set; }

    /// <summary>Motivo de la resolución (obligatorio al rechazar; nota opcional al aprobar).</summary>
    public string? MotivoResolucion { get; private set; }

    private SolicitudCancelacion() { } // EF Core

    internal SolicitudCancelacion(
        MotivoCancelacion motivo,
        IniciadorCancelacion iniciador,
        PenalidadSugerida penalidad,
        DateOnly fechaSolicitud)
    {
        MotivoCategoria = motivo.Categoria;
        MotivoDetalle = motivo.Detalle;
        IniciadaPor = iniciador;
        PenalidadPorcentaje = penalidad.Porcentaje;
        FechaSolicitud = fechaSolicitud;
    }

    /// <summary>Reconstruye el VO de penalidad congelada (SUGERIDA) a partir del porcentaje persistido.</summary>
    public PenalidadSugerida Penalidad => PenalidadSugerida.Crear(PenalidadPorcentaje);

    /// <summary>Indica si el episodio ya fue resuelto (aprobado o rechazado).</summary>
    public bool EstaResuelta => Resultado is not null;

    /// <summary>Registra una APROBACIÓN con la penalidad efectivamente aplicada (override si difiere de la congelada).</summary>
    internal void RegistrarAprobacion(string resueltaPor, DateOnly fechaResolucion, decimal penalidadAplicadaPorcentaje, string? nota)
    {
        ResueltaPor = resueltaPor;
        FechaResolucion = fechaResolucion;
        Resultado = ResultadoResolucion.Aprobada;
        PenalidadAplicadaPorcentaje = penalidadAplicadaPorcentaje;
        PenalidadFueOverride = penalidadAplicadaPorcentaje != PenalidadPorcentaje;
        MotivoResolucion = nota;
    }

    /// <summary>Registra un RECHAZO con su motivo (obligatorio).</summary>
    internal void RegistrarRechazo(string resueltaPor, DateOnly fechaResolucion, string motivo)
    {
        ResueltaPor = resueltaPor;
        FechaResolucion = fechaResolucion;
        Resultado = ResultadoResolucion.Rechazada;
        MotivoResolucion = motivo;
    }
}
