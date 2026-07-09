using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Web;
using Reservas.Application.Reservas.BuscarDisponibilidad;
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

// Caché de lectura de disponibilidad (Story 3.2): Redis si está configurado (Aspire lo referencia como "redis");
// si no, caché en memoria para desarrollo local sin orquestador. IDistributedCache es la abstracción común.
var cadenaRedis = builder.Configuration.GetConnectionString("redis");
if (!string.IsNullOrWhiteSpace(cadenaRedis))
{
    builder.Services.AddStackExchangeRedisCache(opciones => opciones.Configuration = cadenaRedis);
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

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

// CAP-4 · Búsqueda de habitaciones disponibles (FR-8). Query CQRS servida desde la proyección + caché Redis.
app.MapGet("/api/v1/habitaciones/disponibles", async (
        string ciudad, DateOnly entrada, DateOnly salida, int huespedes, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(new BuscarDisponibilidadQuery(ciudad, entrada, salida, huespedes), ct);
        return resultado.ToOkResult();
    })
    .WithName("BuscarDisponibilidad")
    .WithTags("Habitaciones");

app.Run();
