using Dapr;
using HotelBookingHub.Comun.Eventos;
using Notificaciones.Worker;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// Latido de fondo del worker (se reemplaza por el consumo real de eventos en la Épica 5).
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

// Desenvuelve los CloudEvents que entrega Dapr y expone el endpoint de suscripciones (/dapr/subscribe).
app.UseCloudEvents();
app.MapSubscribeHandler();

// /health y /alive del worker (Story 1.1).
app.MapDefaultEndpoints();

// Suscripción de HUMO (Story 1.1): consume el evento publicado por Reservas para verificar el
// salto asíncrono de punta a punta. TEMPORAL: la suscripción real (ReservaConfirmada, cancelaciones)
// con idempotencia por message-id llega en la Épica 5.
app.MapPost("/smoke", (EventoIntegracion evento, ILogger<Program> logger) =>
{
    logger.LogInformation(
        "Worker recibió evento de humo por Dapr pub/sub: Id={Id} Type={Type}",
        evento.Id, evento.Type);
    return Results.Ok();
}).WithTopic("pubsub", "smoke");

app.Run();

// Marcador para el binding de tipos genéricos del logger en top-level statements.
public partial class Program;
