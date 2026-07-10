using HotelBookingHub.Comun.Web.Seguridad;
using Microsoft.IdentityModel.Tokens;

namespace HotelBookingHub.Comun.Web.UnitTests;

/// <summary>
/// Story 6.1 — el helper compartido de validación JWT debe activar las cuatro validaciones de seguridad
/// (issuer/audience/lifetime/firma) y acotar el clock skew. Es dominio puro de configuración, unit-testeable
/// sin levantar un host (el 401 end-to-end se prueba a nivel HTTP en el test funcional del Gateway).
/// </summary>
public class AutenticacionJwtTests
{
    private static OpcionesJwt OpcionesValidas() => new()
    {
        Issuer = "hotel-booking-hub",
        Audience = "hotel-booking-hub-api",
        SigningKey = "clave-de-prueba-suficientemente-larga-para-hmac-sha256-256bits",
    };

    [Fact]
    public void ConstruirParametrosValidacion_activa_las_cuatro_validaciones()
    {
        var parametros = AutenticacionJwtExtensions.ConstruirParametrosValidacion(OpcionesValidas());

        Assert.True(parametros.ValidateIssuer);
        Assert.True(parametros.ValidateAudience);
        Assert.True(parametros.ValidateLifetime);
        Assert.True(parametros.ValidateIssuerSigningKey);
        Assert.Equal("hotel-booking-hub", parametros.ValidIssuer);
        Assert.Equal("hotel-booking-hub-api", parametros.ValidAudience);
        Assert.NotNull(parametros.IssuerSigningKey);
    }

    [Fact]
    public void ConstruirParametrosValidacion_fija_el_clock_skew_en_1_min()
    {
        var parametros = AutenticacionJwtExtensions.ConstruirParametrosValidacion(OpcionesValidas());

        // Guarda el endurecimiento explícito (default de la librería = 5 min): revertirlo debe romper el test.
        Assert.Equal(TimeSpan.FromMinutes(1), parametros.ClockSkew);
    }

    [Fact]
    public void ConstruirParametrosValidacion_fija_el_algoritmo_a_HS256()
    {
        var parametros = AutenticacionJwtExtensions.ConstruirParametrosValidacion(OpcionesValidas());

        Assert.NotNull(parametros.ValidAlgorithms);
        Assert.Equal([SecurityAlgorithms.HmacSha256], parametros.ValidAlgorithms);
    }

    [Fact]
    public void ConstruirParametrosValidacion_sin_clave_de_firma_falla()
    {
        var opciones = OpcionesValidas() with { SigningKey = "" };

        Assert.Throws<InvalidOperationException>(
            () => AutenticacionJwtExtensions.ConstruirParametrosValidacion(opciones));
    }

    [Fact]
    public void ConstruirParametrosValidacion_con_clave_corta_falla()
    {
        // 16 bytes < 32 bytes (256 bits) requeridos por HMAC-SHA256.
        var opciones = OpcionesValidas() with { SigningKey = "clave-corta-1234" };

        Assert.Throws<InvalidOperationException>(
            () => AutenticacionJwtExtensions.ConstruirParametrosValidacion(opciones));
    }

    [Fact]
    public void ConstruirParametrosValidacion_sin_issuer_falla()
    {
        var opciones = OpcionesValidas() with { Issuer = "" };

        Assert.Throws<InvalidOperationException>(
            () => AutenticacionJwtExtensions.ConstruirParametrosValidacion(opciones));
    }

    [Fact]
    public void ConstruirParametrosValidacion_sin_audience_falla()
    {
        var opciones = OpcionesValidas() with { Audience = "" };

        Assert.Throws<InvalidOperationException>(
            () => AutenticacionJwtExtensions.ConstruirParametrosValidacion(opciones));
    }
}
