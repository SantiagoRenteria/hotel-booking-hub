using Notificaciones.Worker;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// El worker consume eventos (E5). Se aloja en un host web mínimo para exponer /health de forma uniforme.
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

// /health y /alive del worker (Story 1.1). La suscripción a eventos por Dapr llega en E5.
app.MapDefaultEndpoints();

app.Run();
