var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// API Gateway (YARP): único punto de entrada. Enruta/agrega hacia los BC; SIN lógica de negocio.
// La autenticación JWT, el rate limiting y HTTPS enforcement se añaden en la Épica 6 (seguridad).
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// /health y /alive del propio Gateway (no se rutean al exterior).
app.MapDefaultEndpoints();

app.MapReverseProxy();

app.Run();

// Expone la clase Program para WebApplicationFactory (tests funcionales de autenticación, Story 6.1).
public partial class Program;
