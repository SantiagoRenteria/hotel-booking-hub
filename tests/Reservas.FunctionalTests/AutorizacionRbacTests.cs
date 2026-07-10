using System.Net;
using System.Net.Http.Headers;
using TestKit.Auth;

namespace Reservas.FunctionalTests;

/// <summary>
/// Story 6.2 · AC-E6.2.1/.2 — el RBAC se resuelve server-side en cada servicio: un rol sin permiso recibe
/// <c>403</c> (autenticado pero no autorizado, distinto del 401 de 6.1); el rol correcto pasa la autorización.
/// Prueba a nivel HTTP con WebApplicationFactory (el 403 ocurre antes del handler → sin base de datos).
/// </summary>
public class AutorizacionRbacTests(ReservasApiFactory factory) : IClassFixture<ReservasApiFactory>
{
    // SoloAgente: el listado de reservas del agente.
    private const string RutaSoloAgente = "/api/v1/reservas";

    // AgenteOViajero: la búsqueda de disponibilidad.
    private const string RutaAgenteOViajero =
        "/api/v1/habitaciones/disponibles?ciudad=Bogota&entrada=2026-08-01&salida=2026-08-03&huespedes=2";

    [Fact]
    public async Task Viajero_en_endpoint_SoloAgente_responde_403()
    {
        var respuesta = await Get(RutaSoloAgente, TokenDePrueba.RolViajero);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Token_sin_rol_en_endpoint_SoloAgente_responde_403()
    {
        // Token válidamente firmado/emitido (autenticado) pero SIN claim de rol → 403, no 401.
        var respuesta = await Get(RutaSoloAgente, rol: null);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Agente_en_endpoint_AgenteOViajero_no_es_rechazado_por_autorizacion()
    {
        var respuesta = await Get(RutaAgenteOViajero, TokenDePrueba.RolAgente);

        Assert.NotEqual(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Viajero_en_endpoint_AgenteOViajero_no_es_rechazado_por_autorizacion()
    {
        var respuesta = await Get(RutaAgenteOViajero, TokenDePrueba.RolViajero);

        Assert.NotEqual(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    private async Task<HttpResponseMessage> Get(string ruta, string? rol)
    {
        var cliente = factory.CreateClient();
        var token = TokenDePrueba.Emitir(rol: rol);
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await cliente.GetAsync(ruta);
    }
}
