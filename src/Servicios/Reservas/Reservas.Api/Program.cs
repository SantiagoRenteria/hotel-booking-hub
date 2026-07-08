using System.Diagnostics;
using Dapr.Client;
using HotelBookingHub.Comun.Eventos;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// OpenAPI nativo de .NET 10 (sin Swashbuckle). La UI Scalar se añade cuando haya endpoints de negocio.
builder.Services.AddOpenApi();

// Cliente Dapr para publicar eventos de integración (pub/sub). El outbox transaccional llega en 1.6b.
builder.Services.AddDaprClient();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// /health y /alive (Story 1.1). Los endpoints de negocio de Reservas (CAP-4/5/6) llegan en la Story 1.6a.
app.MapDefaultEndpoints();

// Endpoint de HUMO (Story 1.1): publica un evento por Dapr pub/sub para probar el salto asíncrono
// de punta a punta hacia Notificaciones.Worker. TEMPORAL: se retira cuando llegue el evento real
// de reserva (ReservaConfirmada) en la Story 1.6.
app.MapPost("/_smoke/ping", async (DaprClient dapr, CancellationToken ct) =>
{
    var evento = new EventoIntegracion(
        Id: Guid.CreateVersion7(),
        Type: "SmokePing.v1",
        Version: 1,
        OccurredAt: DateTimeOffset.UtcNow,
        TraceId: Activity.Current?.TraceId.ToString(),
        Data: new { mensaje = "hola desde Reservas" });

    await dapr.PublishEventAsync("pubsub", "smoke", evento, ct);
    return Results.Accepted($"/_smoke/ping/{evento.Id}", evento.Id);
});

app.Run();
