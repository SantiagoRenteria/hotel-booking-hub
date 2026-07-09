using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Web;
using Hoteles.Application.Hoteles.CrearHotel;
using Hoteles.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// OpenAPI nativo de .NET 10 (sin Swashbuckle). La UI Scalar se añade cuando haya endpoints de negocio.
builder.Services.AddOpenApi();

// Pipeline del mediator (Logging → Validation → Handler) + validators, por scan del assembly de Application.
builder.Services.AddMediatorPipeline(typeof(CrearHotelCommand).Assembly);

// Adaptadores de infraestructura (DbContext + repositorio).
builder.Services.AddHotelesInfrastructure(builder.Configuration.GetConnectionString("hotelesdb"));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// HTTPS enforcement es responsabilidad del API Gateway (borde único); los servicios corren HTTP tras él.

// /health y /alive (Story 1.1).
app.MapDefaultEndpoints();

// CAP-1 · Crear hotel (FR-1). Mapeo Result→HTTP centralizado (union type explícito).
app.MapPost("/api/v1/hoteles", async (CrearHotelCommand comando, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(comando, ct);
        return resultado.ToCreatedResult(dto => $"/api/v1/hoteles/{dto.Id}");
    })
    .WithName("CrearHotel")
    .WithTags("Hoteles");

app.Run();
