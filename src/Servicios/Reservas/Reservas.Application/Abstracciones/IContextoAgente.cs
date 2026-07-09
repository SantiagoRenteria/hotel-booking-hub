namespace Reservas.Application.Abstracciones;

/// <summary>
/// Identidad del agente que realiza la petición, resuelta <b>server-side</b> (Story 3.3, Task 0). Es la costura
/// que aísla el mecanismo de identidad: hoy la provee una cabecera (<c>X-Agente</c>) como puente explícito; en la
/// Épica 6 (seguridad) se reemplaza por un claim del token de auth SIN tocar los handlers ni las queries.
/// <para><b>Fail-closed:</b> si no hay identidad, <see cref="AgenteActual"/> es null y la consulta NO devuelve
/// todo — el handler corta con 403. Esto NO es autenticación; es un puente de identidad, deuda de la Épica 6.</para>
/// </summary>
public interface IContextoAgente
{
    /// <summary>Correo del agente actual, o null si la petición no trae identidad resoluble.</summary>
    string? AgenteActual { get; }
}
