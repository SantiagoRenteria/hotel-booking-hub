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

    /// <summary>Reconstruye el VO de penalidad congelada a partir del porcentaje persistido.</summary>
    public PenalidadSugerida Penalidad => PenalidadSugerida.Crear(PenalidadPorcentaje);
}
