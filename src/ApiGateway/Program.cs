using System.Threading.RateLimiting;
using HotelBookingHub.Comun.Web.Seguridad;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// Autenticación JWT en el borde (Story 6.1, AC-E6.1.1): sin token válido → 401 antes de rutear.
builder.Services.AddAutenticacionJwt(builder.Configuration);

// Rate limiting (Story 6.4, práctica OWASP #3 · A07/A04): sliding window por cliente (IP) → exceso = 429.
// Protege login/búsqueda de abuso y fuerza bruta. Límites configurables (RateLimit:*) con defaults sensatos.
var permitidos = builder.Configuration.GetValue<int?>("RateLimit:PermitLimit") ?? 100;
var ventanaSegundos = builder.Configuration.GetValue<int?>("RateLimit:WindowSeconds") ?? 60;
builder.Services.AddRateLimiter(opciones =>
{
    opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opciones.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permitidos,
                Window = TimeSpan.FromSeconds(ventanaSegundos),
                SegmentsPerWindow = 4,
                QueueLimit = 0,
            }));
});

// CORS con lista explícita (Story 6.4, práctica #6 · A05): NUNCA AllowAnyOrigin. Orígenes desde config.
var origenes = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(opciones => opciones.AddDefaultPolicy(politica =>
    politica.WithOrigins(origenes).AllowAnyHeader().AllowAnyMethod()));

// API Gateway (YARP): único punto de entrada. Enruta/agrega hacia los BC; SIN lógica de negocio.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// HSTS (Story 6.4, práctica #6 · A05): instruye al navegador a usar solo HTTPS. La TERMINACIÓN TLS y la
// redirección a HTTPS viven en el ingress (ACA/Azure, ADR-008), no en la app: en local/compose el tráfico es
// HTTP intencionalmente. Se emite el header en no-desarrollo (inocuo sobre HTTP; los navegadores lo aplican en HTTPS).
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
