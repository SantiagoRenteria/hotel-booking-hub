using System.Net;
using System.Net.Http.Headers;

namespace Seguridad.FunctionalTests;

/// <summary>
/// Story 6.1 · AC-E6.1.1 / AC-E6.1.2 — el Gateway exige un JWT válido: sin token, o con token
/// inválido/expirado/mal firmado/issuer/audience erróneos → <c>401</c> en el borde. Un token válido NO es
/// rechazado por autenticación (el proxy falla con 5xx porque el downstream no corre en el test; lo que
/// importa es que NO es 401). Prueba a nivel HTTP con WebApplicationFactory (sin base de datos).
/// </summary>
public class AutenticacionGatewayTests(JwtTestFactory factory) : IClassFixture<JwtTestFactory>
{
    private const string RutaProtegida = "/api/v1/reservas";

    private HttpClient Cliente() => factory.CreateClient();

    [Fact]
    public async Task Sin_token_responde_401()
    {
        var respuesta = await Cliente().GetAsync(RutaProtegida);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task El_401_respeta_el_contrato_de_error_del_sistema()
    {
        var respuesta = await Cliente().GetAsync(RutaProtegida);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        // Problem Details RFC 7807 + reto Bearer (AC-E6.1.1).
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Bearer", respuesta.Headers.WwwAuthenticate.ToString());

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":401", cuerpo);
        Assert.Contains("traceId", cuerpo);
    }

    [Fact]
    public async Task Token_expirado_responde_401()
    {
        var token = JwtTestFactory.EmitirToken(expira: DateTime.UtcNow.AddHours(-1));

        var respuesta = await EnviarConToken(token);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Token_con_firma_invalida_responde_401()
    {
        var token = JwtTestFactory.EmitirToken(signingKey: "otra-clave-distinta-que-no-coincide-con-la-real-256b");

        var respuesta = await EnviarConToken(token);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Token_con_issuer_incorrecto_responde_401()
    {
        var token = JwtTestFactory.EmitirToken(issuer: "emisor-no-confiable");

        var respuesta = await EnviarConToken(token);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Token_con_audience_incorrecto_responde_401()
    {
        var token = JwtTestFactory.EmitirToken(audience: "otra-audiencia");

        var respuesta = await EnviarConToken(token);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Token_valido_pasa_la_autenticacion_y_se_rutea_al_proxy()
    {
        var token = JwtTestFactory.EmitirToken();

        var respuesta = await EnviarConToken(token);

        // Autenticado → NO 401. Como el downstream no corre en el test, el proxy YARP falla con 5xx:
        // aseverar >= 500 distingue "pasó auth y se enrutó" de un 4xx que enmascararía un pipeline roto.
        Assert.NotEqual(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.True((int)respuesta.StatusCode >= 500, $"Se esperaba 5xx del proxy, no {(int)respuesta.StatusCode}.");
    }

    private async Task<HttpResponseMessage> EnviarConToken(string token)
    {
        var cliente = Cliente();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await cliente.GetAsync(RutaProtegida);
    }
}
