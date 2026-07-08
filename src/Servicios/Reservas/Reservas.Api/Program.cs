var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// OpenAPI nativo de .NET 10 (sin Swashbuckle). La UI Scalar se añade cuando haya endpoints de negocio.
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// /health y /alive (Story 1.1). Los endpoints de negocio de Reservas (CAP-4/5/6) y la publicación
// real de eventos por Dapr (outbox → ReservaConfirmada) llegan en la Story 1.6.
app.MapDefaultEndpoints();

app.Run();
