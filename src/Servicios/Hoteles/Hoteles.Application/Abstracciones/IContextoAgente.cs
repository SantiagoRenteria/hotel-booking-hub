namespace Hoteles.Application.Abstracciones;

/// <summary>
/// Identidad del agente que realiza la petición, resuelta <b>server-side</b> desde el claim <c>email</c> del
/// token JWT validado (Story 6.3). Es la costura que aísla el mecanismo de identidad del resto del BC de Hoteles.
/// El aislamiento por propietario se aplica de forma centralizada en <c>HotelesDbContext</c> (query filter),
/// que consume esta identidad; un hotel de otro agente queda invisible → carga-para-mutar → 404.
/// <para><b>Fail-closed:</b> sin identidad, <see cref="AgenteActual"/> es null.</para>
/// </summary>
public interface IContextoAgente
{
    /// <summary>Correo (canónico) del agente actual, o null si la petición no trae identidad resoluble.</summary>
    string? AgenteActual { get; }
}
