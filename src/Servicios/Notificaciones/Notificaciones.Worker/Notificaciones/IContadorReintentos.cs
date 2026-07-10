namespace Notificaciones.Worker.Notificaciones;

/// <summary>
/// Cuenta los intentos de procesamiento por mensaje (Story 5.1b Task 4) para detectar el veneno y acotar el
/// re-reclamo. Con un worker distribuido el contador debe ser compartido (Redis <c>INCR</c>+TTL); el fallback en
/// memoria (<see cref="ContadorReintentosEnMemoria"/>) es fiel en single-instance.
/// </summary>
public interface IContadorReintentos
{
    /// <summary>Incrementa y devuelve el nº de intentos acumulados para <paramref name="messageId"/> (1 en el 1er fallo).</summary>
    Task<long> IncrementarAsync(Guid messageId, CancellationToken ct);

    /// <summary>Reinicia el contador tras un procesamiento con éxito (el mensaje dejó de ser candidato a veneno).</summary>
    Task ReiniciarAsync(Guid messageId, CancellationToken ct);
}
