using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace Seguridad.FunctionalTests;

/// <summary>
/// Story T.2 · AC-ET.2.3 — regresión de ruteo del Gateway. La búsqueda de disponibilidad
/// (<c>GET /api/v1/habitaciones/disponibles</c>) la sirve el BC de <b>Reservas</b>, no Hoteles; sin una ruta
/// específica, el catch-all <c>/api/v1/habitaciones/{**catch-all}</c> la mandaría al cluster <c>hoteles</c>
/// (404). Un test HTTP no puede distinguir el cluster (ambos downstream están caídos en el test → 5xx), así que
/// se asevera directamente la tabla de rutas de YARP vía <see cref="IProxyConfigProvider"/>: la ruta específica
/// va a <c>reservas</c> y el resto de <c>habitaciones</c> permanece en <c>hoteles</c>.
/// </summary>
public class RuteoGatewayTests(JwtTestFactory factory) : IClassFixture<JwtTestFactory>
{
    private IProxyConfig Config() =>
        factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();

    [Fact]
    public void La_busqueda_de_disponibilidad_se_rutea_al_cluster_reservas()
    {
        var ruta = Config().Routes.SingleOrDefault(r => r.Match.Path == "/api/v1/habitaciones/disponibles");

        Assert.NotNull(ruta);
        Assert.Equal("reservas", ruta!.ClusterId);
    }

    [Fact]
    public void El_resto_de_habitaciones_permanece_en_el_cluster_hoteles()
    {
        var catchAll = Config().Routes.SingleOrDefault(r => r.Match.Path == "/api/v1/habitaciones/{**catch-all}");

        Assert.NotNull(catchAll);
        Assert.Equal("hoteles", catchAll!.ClusterId);
    }
}
