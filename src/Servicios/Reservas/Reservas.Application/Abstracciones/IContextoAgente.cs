namespace Reservas.Application.Abstracciones;

/// <summary>
/// Identidad del agente que realiza la petición, resuelta <b>server-side</b>. Es la costura que aísla el
/// mecanismo de identidad: desde la Story 6.3 la provee el claim <c>email</c> del token JWT validado (Story 6.1);
/// antes era un puente por cabecera <c>X-Agente</c> (Story 3.3), ya retirado. El cambio de fuente NO tocó
/// handlers ni queries — dependen de esta interfaz.
/// <para><b>Fail-closed:</b> si no hay identidad, <see cref="AgenteActual"/> es null y la consulta NO devuelve
/// todo — el handler corta con 403/404.</para>
/// </summary>
public interface IContextoAgente
{
    /// <summary>Correo del agente actual, o null si la petición no trae identidad resoluble.</summary>
    string? AgenteActual { get; }
}
