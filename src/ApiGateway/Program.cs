using HotelBookingHub.Comun.Web.Seguridad;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// Autenticación JWT en el borde (Story 6.1, AC-E6.1.1): sin token válido → 401 antes de rutear.
builder.Services.AddAutenticacionJwt(builder.Configuration);

// API Gateway (YARP): único punto de entrada. Enruta/agrega hacia los BC; SIN lógica de negocio.
// El rate limiting y HTTPS enforcement se añaden en la Story 6.4 (endurecimiento OWASP).
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

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
