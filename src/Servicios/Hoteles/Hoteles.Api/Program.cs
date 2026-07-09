using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Web;
using Hoteles.Application.Hoteles.CrearHotel;
using Hoteles.Application.Hoteles.EditarHotel;
using Hoteles.Application.Hoteles.EliminarHotel;
using Hoteles.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// OpenAPI nativo de .NET 10 (sin Swashbuckle). La UI Scalar se añade cuando haya endpoints de negocio.
builder.Services.AddOpenApi();

// Excepciones de negocio → Problem Details RFC 7807 (handler transversal en Comun.Web; concurrencia → 409).
builder.Services.AddManejoExcepcionesNegocio();

// Pipeline del mediator (Logging → Validation → Handler) + validators, por scan del assembly de Application.
builder.Services.AddMediatorPipeline(typeof(CrearHotelCommand).Assembly);

// Adaptadores de infraestructura (DbContext + repositorio).
builder.Services.AddHotelesInfrastructure(builder.Configuration.GetConnectionString("hotelesdb"));

var app = builder.Build();

app.UseExceptionHandler();

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

// CAP-1 · Editar hotel (AC-E2.2.1). El id de la ruta manda sobre el del cuerpo (evita discrepancias). El
// rowVersion del cuerpo arbitra la concurrencia optimista (409 si pierde la carrera; el handler transversal
// lo traduce). 404 si no existe o fue eliminado.
app.MapPut("/api/v1/hoteles/{id:guid}", async (Guid id, EditarHotelCommand comando, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(comando with { Id = id }, ct);
        return resultado.ToOkResult();
    })
    .WithName("EditarHotel")
    .WithTags("Hoteles");

// CAP-1 · Eliminar hotel — baja lógica (AC-E2.2.2). El rowVersion (cuerpo) arbitra la concurrencia. 204 al
// eliminar; 404 si no existe o ya estaba eliminado; 409 si pierde una carrera contra otra edición/baja.
app.MapDelete("/api/v1/hoteles/{id:guid}", async (Guid id, EliminarHotelCommand comando, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(comando with { Id = id }, ct);
        return resultado.ToNoContentResult();
    })
    .WithName("EliminarHotel")
    .WithTags("Hoteles");

app.Run();
