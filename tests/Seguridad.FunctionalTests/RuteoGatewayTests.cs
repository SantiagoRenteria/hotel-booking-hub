using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace Seguridad.FunctionalTests;

/// <summary>
/// Story T.2 · AC-ET.2.3 — regresión de ruteo del Gateway. La búsqueda de disponibilidad
/// (<c>GET /api/v1/habitaciones/disponibles</c>) la sirve el BC de <b>Reservas</b>, no Hoteles; sin una ruta
/// específica, el catch-all <c>/api/v1/habitaciones/{**catch-all}</c> la mandaría al cluster <c>hoteles</c>
/// (404). Un test HTTP no puede distinguir el cluster (ambos downstream están caídos en el test → 5xx), así que
/// se asevera directamente la tabla de rutas de YARP vía <see cref="IProxyConfigProvider"/>: la ruta específica
/// va a <c>reservas</c>, el resto de <c>habitaciones</c> permanece en <c>hoteles</c>, y ninguna fija un
/// <c>Order</c> que invierta la precedencia por especificidad (literal &gt; catch-all). La resolución de la
/// precedencia real de YARP para una petición se verifica end-to-end en el smoke + Newman (job <c>smoke-compose</c>,
/// stack completo): <c>disponibles</c> responde 200 (ruteó a Reservas), no 404 (Hoteles).
/// </summary>
public class RuteoGatewayTests(JwtTestFactory factory) : IClassFixture<JwtTestFactory>
{
    private const string RutaDisponibles = "/api/v1/habitaciones/disponibles";
    private const string RutaCatchAll = "/api/v1/habitaciones/{**catch-all}";

    private IProxyConfig Config() =>
        factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();

    [Fact]
    public void La_busqueda_de_disponibilidad_se_rutea_al_cluster_reservas()
    {
        var ruta = Config().Routes.SingleOrDefault(r => r.Match.Path == RutaDisponibles);

        Assert.NotNull(ruta);
        Assert.Equal("reservas", ruta!.ClusterId);
    }

    [Fact]
    public void El_resto_de_habitaciones_permanece_en_el_cluster_hoteles()
    {
        var catchAll = Config().Routes.SingleOrDefault(r => r.Match.Path == RutaCatchAll);

        Assert.NotNull(catchAll);
        Assert.Equal("hoteles", catchAll!.ClusterId);
    }

    [Fact]
    public void Ninguna_ruta_de_habitaciones_fija_un_Order_que_invierta_la_precedencia()
    {
        var rutas = Config().Routes;
        var disponibles = rutas.Single(r => r.Match.Path == RutaDisponibles);
        var catchAll = rutas.Single(r => r.Match.Path == RutaCatchAll);

        // Sin Order explícito, YARP prioriza por especificidad del patrón (literal gana al catch-all). Un Order
        // en el catch-all (p. ej. -1) lo haría ganar para /disponibles → 404 en Hoteles, dejando los otros tests
        // verdes. Este aserto bloquea esa regresión de precedencia.
        Assert.Null(disponibles.Order);
        Assert.Null(catchAll.Order);
    }
}
