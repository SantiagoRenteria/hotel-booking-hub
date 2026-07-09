using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Web;
using Reservas.Application.Reservas.CrearReserva;
using Reservas.Domain.Servicios;
using Reservas.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// OpenAPI nativo de .NET 10 (sin Swashbuckle). La UI Scalar se añade cuando haya endpoints de negocio.
builder.Services.AddOpenApi();

// Excepciones de negocio → Problem Details RFC 7807 (handler transversal en Comun.Web; overbooking → 409).
builder.Services.AddManejoExcepcionesNegocio();

// Pipeline del mediator (Logging → Validation → Handler) + validators, por scan del assembly de Application.
builder.Services.AddMediatorPipeline(typeof(CrearReservaCommand).Assembly);

// Domain service de precio (puro, sin estado).
builder.Services.AddSingleton<CalculadorPrecio>();

// Adaptadores de infraestructura (DbContext + repositorio + placeholders de disponibilidad/eventos).
builder.Services.AddReservasInfrastructure(builder.Configuration.GetConnectionString("reservasdb"));

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// HTTPS enforcement es responsabilidad del API Gateway (borde único); los servicios corren HTTP tras él.

// /health y /alive (Story 1.1).
app.MapDefaultEndpoints();

// CAP-5 · Crear-confirmar reserva (FR-9/10/11). Mapeo Result→HTTP centralizado (union type explícito).
app.MapPost("/api/v1/reservas", async (CrearReservaCommand comando, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(comando, ct);
        return resultado.ToCreatedResult(dto => $"/api/v1/reservas/{dto.Id}");
    })
    .WithName("CrearReserva")
    .WithTags("Reservas");

app.Run();
