using HotelBookingHub.Comun.Web.Seguridad;

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
    public void ConstruirParametrosValidacion_acota_el_clock_skew_a_lo_sumo_5_min()
    {
        var parametros = AutenticacionJwtExtensions.ConstruirParametrosValidacion(OpcionesValidas());

        // El default de la librería es 5 min; exigimos que NO se relaje por encima de eso.
        Assert.True(parametros.ClockSkew <= TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void ConstruirParametrosValidacion_sin_clave_de_firma_falla()
    {
        var opciones = OpcionesValidas() with { SigningKey = "" };

        Assert.Throws<InvalidOperationException>(
            () => AutenticacionJwtExtensions.ConstruirParametrosValidacion(opciones));
    }
}
