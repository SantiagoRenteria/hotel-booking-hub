namespace Reservas.Domain.Reservas;

/// <summary>
/// Quién inicia la solicitud de cancelación (auditoría del ciclo de vida, Épica 4). El viajero puede
/// solicitarla directamente o el agente en su nombre (FR de intermediación). Se persiste en la
/// <see cref="SolicitudCancelacion"/> y viaja en el evento de integración.
/// </summary>
public enum IniciadorCancelacion
{
    Viajero = 1,
    Agente = 2,
}
