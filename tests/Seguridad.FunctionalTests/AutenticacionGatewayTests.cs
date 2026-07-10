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
    public async Task Token_valido_no_es_rechazado_por_autenticacion()
    {
        var token = JwtTestFactory.EmitirToken();

        var respuesta = await EnviarConToken(token);

        // El downstream no corre en el test → el proxy devuelve 5xx; lo relevante es que NO es 401.
        Assert.NotEqual(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    private async Task<HttpResponseMessage> EnviarConToken(string token)
    {
        var cliente = Cliente();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await cliente.GetAsync(RutaProtegida);
    }
}
