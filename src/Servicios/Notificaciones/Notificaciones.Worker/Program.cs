using Notificaciones.Worker;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// Latido de fondo del worker. El consumo real de eventos de dominio (ReservaConfirmada,
// cancelaciones) con suscripción Dapr idempotente por message-id llega en la Épica 5.
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

// /health y /alive del worker (Story 1.1).
app.MapDefaultEndpoints();

app.Run();
