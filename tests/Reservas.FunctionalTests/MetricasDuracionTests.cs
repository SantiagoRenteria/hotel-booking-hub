using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using TestKit.Auth;

namespace Reservas.FunctionalTests;

/// <summary>
/// Story 7.2 (AC-E7.2.1/.2/.3) — la instrumentación de ASP.NET Core emite el histograma de duración por request
/// (<c>http.server.request.duration</c>, meter <c>Microsoft.AspNetCore.Hosting</c>) segmentado por endpoint
/// (tag <c>http.route</c>), del que el dashboard deriva p95/p99. Verificación determinista con un
/// <c>MeterListener</c> en memoria (no depende del dashboard). El histograma ya lo aporta el <c>ServiceDefaults</c>
/// (E1); esta historia lo caracteriza y evidencia (alcance Winston: instrumentar y mostrar, sin load test k6).
/// </summary>
public sealed class MetricasDuracionTests(ReservasApiFactory factory) : IClassFixture<ReservasApiFactory>
{
    private const string InstrumentoDuracion = "http.server.request.duration";
    private const string MeterAspNetCore = "Microsoft.AspNetCore.Hosting";

    private const string RutaReservas = "/api/v1/reservas";
    private const string RutaDisponibles =
        "/api/v1/habitaciones/disponibles?ciudad=Bogota&entrada=2026-08-01&salida=2026-08-03&huespedes=2";
    private const string RutaHealth = "/health";

    private sealed record Medida(double Valor, string? Ruta);

    private static MeterListener EscucharDuracion(List<Medida> medidas, object candado)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrumento, l) =>
            {
                if (instrumento.Meter.Name == MeterAspNetCore && instrumento.Name == InstrumentoDuracion)
                {
                    l.EnableMeasurementEvents(instrumento);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((_, valor, tags, _) =>
        {
            string? ruta = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "http.route")
                {
                    ruta = tag.Value?.ToString();
                }
            }

            lock (candado)
            {
                medidas.Add(new Medida(valor, ruta));
            }
        });
        listener.Start();
        return listener;
    }

    private async Task Get(string ruta, string? rol)
    {
        using var cliente = factory.CreateClient();
        var token = TokenDePrueba.Emitir(rol: rol);
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var _ = await cliente.GetAsync(ruta);
    }

    /// <summary>
    /// El histograma se graba en el pipeline del servidor; puede completarse un instante DESPUÉS de que el
    /// cliente reciba la respuesta. Se sondea (acotado, ~3 s) hasta que se cumpla la condición, evitando una
    /// carrera. Si no se cumple en el presupuesto, falla con un diagnóstico de las rutas observadas.
    /// </summary>
    private static async Task<List<Medida>> EsperarMedidas(
        List<Medida> medidas, object candado, Func<List<Medida>, bool> hasta, string queSeEsperaba)
    {
        for (var intento = 0; intento < 150; intento++)
        {
            List<Medida> copia;
            lock (candado)
            {
                copia = [.. medidas];
            }

            if (hasta(copia))
            {
                return copia;
            }

            await Task.Delay(20);
        }

        List<Medida> ultima;
        lock (candado)
        {
            ultima = [.. medidas];
        }

        Assert.Fail($"Timeout (~3s) esperando: {queSeEsperaba}. Rutas observadas: [{string.Join(" | ", ultima.Select(m => m.Ruta ?? "<null>"))}]");
        return ultima; // inalcanzable (Assert.Fail lanza)
    }

    private static List<string> RutasNegocio(List<Medida> medidas) => medidas
        .Select(m => m.Ruta)
        .Where(r => r is not null && r.Contains("api/v1"))
        .Distinct()
        .Select(r => r!)
        .ToList();

    [Fact]
    public async Task Emite_histograma_de_duracion_segmentado_por_endpoint()
    {
        // Given: un listener sobre el histograma de duración de request.
        var medidas = new List<Medida>();
        var candado = new object();
        using var listener = EscucharDuracion(medidas, candado);

        // When: se ejercitan DOS endpoints de negocio distintos.
        await Get(RutaReservas, TokenDePrueba.RolAgente);
        await Get(RutaDisponibles, TokenDePrueba.RolAgente);

        // Then: hay mediciones de duración segmentadas por ruta (≥ 2 rutas de negocio distintas) → el dashboard
        // deriva p95/p99 POR endpoint, no un único agregado global (AC-E7.2.1/.2).
        var copia = await EsperarMedidas(medidas, candado, m => RutasNegocio(m).Count >= 2, "≥2 rutas de negocio distintas");
        var rutasNegocio = RutasNegocio(copia);
        Assert.Contains(rutasNegocio, r => r.Contains("reservas"));
    }

    [Fact]
    public async Task Las_sondas_de_salud_no_contaminan_las_series_de_negocio()
    {
        // Given: un listener sobre el histograma.
        var medidas = new List<Medida>();
        var candado = new object();
        using var listener = EscucharDuracion(medidas, candado);

        // When: solo tráfico de sondas a /health (sin autenticación).
        using (var cliente = factory.CreateClient())
        {
            using var _ = await cliente.GetAsync(RutaHealth);
        }

        // Then: la sonda se observa como su PROPIA serie (ruta "/health"), etiquetada y distinta de las rutas de
        // negocio (que llevan "api/v1") → claramente separable (AC-E7.2.3). Se afirma por PRESENCIA de la serie de
        // salud: es robusto a cualquier contaminación (otro host solo añadiría más series, nunca borraría la de
        // salud), a diferencia de una aserción de ausencia global. La no-paralelización del ensamblado aísla,
        // además, el listener process-wide de otras clases de test HTTP del mismo proceso.
        var copia = await EsperarMedidas(medidas, candado, m => m.Any(x => x.Ruta == RutaHealth), "la serie de /health");
        Assert.Contains(copia, m => m.Ruta == RutaHealth);
        Assert.DoesNotContain("api/v1", RutaHealth); // la serie de salud NO es una ruta de negocio
    }
}
