using System.Threading.RateLimiting;
using HotelBookingHub.Comun.Web.Seguridad;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// Forwarded headers (Story 6.4, review): tras el ingress (ACA, ADR-008) la app ve la IP/esquema del proxy.
// X-Forwarded-For/Proto restauran la IP REAL del cliente (partición correcta del rate limiter) y Request.IsHttps
// (HSTS efectivo). El Gateway solo es alcanzable a través del ingress (un único hop de confianza), por eso se
// limpian KnownNetworks/KnownProxies; en un despliegue con proxy de IP fija, fijarlos explícitamente.
builder.Services.Configure<ForwardedHeadersOptions>(opciones =>
{
    opciones.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    opciones.KnownIPNetworks.Clear();
    opciones.KnownProxies.Clear();
});

// Autenticación JWT en el borde (Story 6.1, AC-E6.1.1): sin token válido → 401 antes de rutear.
builder.Services.AddAutenticacionJwt(builder.Configuration);

// Rate limiting (Story 6.4, práctica OWASP #3 · A07/A04): sliding window por cliente (IP real tras ForwardedHeaders)
// → exceso = 429. Protege login/búsqueda de abuso y fuerza bruta. Límites configurables (RateLimit:*).
var permitidos = builder.Configuration.GetValue<int?>("RateLimit:PermitLimit") ?? 100;
var ventanaSegundos = builder.Configuration.GetValue<int?>("RateLimit:WindowSeconds") ?? 60;
if (permitidos <= 0 || ventanaSegundos <= 0)
{
    throw new InvalidOperationException("RateLimit mal configurado: PermitLimit y WindowSeconds deben ser > 0.");
}

builder.Services.AddRateLimiter(opciones =>
{
    opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // El 429 emite Problem Details RFC 7807 + Retry-After (coherente con el 401/403 del sistema).
    opciones.OnRejected = async (contexto, ct) =>
    {
        var respuesta = contexto.HttpContext.Response;
        respuesta.StatusCode = StatusCodes.Status429TooManyRequests;
        respuesta.Headers.RetryAfter = ventanaSegundos.ToString();
        respuesta.ContentType = "application/problem+json"; // RFC 7807, coherente con el 401/403 del sistema
        var problema = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "https://datatracker.ietf.org/doc/html/rfc7807",
            title = "Demasiadas peticiones",
            status = StatusCodes.Status429TooManyRequests,
            detail = "Se superó el límite de peticiones. Reintente más tarde.",
        });
        await respuesta.WriteAsync(problema, ct);
    };
    opciones.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
    {
        // Health/alive EXENTOS: las sondas del orquestador (ACA/K8s) no deben recibir 429 ni provocar reinicios.
        var ruta = contexto.Request.Path;
        if (ruta.StartsWithSegments("/health") || ruta.StartsWithSegments("/alive"))
        {
            return RateLimitPartition.GetNoLimiter("health");
        }

        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permitidos,
                Window = TimeSpan.FromSeconds(ventanaSegundos),
                SegmentsPerWindow = 4,
                QueueLimit = 0,
            });
    });
});

// CORS con lista explícita (Story 6.4, práctica #6 · A05): NUNCA AllowAnyOrigin. Orígenes desde config
// (allowlist VACÍA por defecto = ningún origen cross-site permitido; se puebla por entorno).
var origenes = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(opciones => opciones.AddDefaultPolicy(politica =>
    politica.WithOrigins(origenes).AllowAnyHeader().AllowAnyMethod()));

// HSTS más estricto (Story 6.4, review): incluye subdominios y max-age de 1 año.
builder.Services.AddHsts(opciones =>
{
    opciones.IncludeSubDomains = true;
    opciones.MaxAge = TimeSpan.FromDays(365);
});

// API Gateway (YARP): único punto de entrada. Enruta/agrega hacia los BC; SIN lógica de negocio.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Restaura IP/esquema reales del cliente detrás del ingress (debe ir ANTES de rate limiter/HSTS).
app.UseForwardedHeaders();

// HSTS (Story 6.4, práctica #6 · A05): instruye al navegador a usar solo HTTPS. La TERMINACIÓN TLS y la
// redirección viven en el ingress (ACA/Azure, ADR-008); en local/compose el tráfico es HTTP intencionalmente.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseRateLimiter();
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

// /health y /alive del propio Gateway (no se rutean al exterior; sin autenticación).
app.MapDefaultEndpoints();

// Toda ruta de negocio ruteada exige autenticación (401 en el borde). El RBAC por rol se resuelve
// server-side en cada servicio (Story 6.2); aquí solo se exige un token válido.
app.MapReverseProxy().RequireAuthorization();

app.Run();

// Expone la clase Program para WebApplicationFactory (tests funcionales de autenticación, Story 6.1).
public partial class Program;
