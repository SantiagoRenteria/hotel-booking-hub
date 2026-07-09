using Notificaciones.Worker;
using Notificaciones.Worker.Notificaciones;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// Latido de fondo del worker.
builder.Services.AddHostedService<Worker>();

// Notificaciones (Story 5.1a): sink de Fase 1 (consola/MailHog) + consumidor de ReservaConfirmada.
// La suscripción al transporte real (Dapr pub/sub) sigue diferida como en el resto del sistema
// (PublicadorEventosLog es placeholder); el consumidor se invoca con el envelope (patrón de 3.1).
builder.Services.AddSingleton<INotificador, NotificadorConsola>();
builder.Services.AddSingleton<ConsumidorReservaConfirmada>();

var app = builder.Build();

// /health y /alive del worker (Story 1.1).
app.MapDefaultEndpoints();

app.Run();
