using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Web;
using HotelBookingHub.Comun.Web.Seguridad;
using Hoteles.Application.Habitaciones.CambiarEstadoHabitacion;
using Hoteles.Application.Habitaciones.CrearHabitacion;
using Hoteles.Application.Habitaciones.EditarHabitacion;
using Hoteles.Application.Hoteles.CambiarEstadoHotel;
using Hoteles.Application.Hoteles.CrearHotel;
using Hoteles.Application.Hoteles.EditarHotel;
using Hoteles.Application.Hoteles.EliminarHotel;
using Hoteles.Domain.Habitaciones;
using Hoteles.Domain.Hoteles;
using Hoteles.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// OpenAPI nativo de .NET 10 (sin Swashbuckle). La UI Scalar se añade cuando haya endpoints de negocio.
builder.Services.AddOpenApi();

// Autenticación JWT (Story 6.1, defensa en profundidad): el servicio valida el token además del Gateway.
// El rol del claim habilita el RBAC de 6.2; la identidad, el aislamiento de propiedad de 6.3.
builder.Services.AddAutenticacionJwt(builder.Configuration);

// Excepciones de negocio → Problem Details RFC 7807 (handler transversal en Comun.Web; concurrencia → 409).
builder.Services.AddManejoExcepcionesNegocio();

// Pipeline del mediator (Logging → Validation → Handler) + validators, por scan del assembly de Application.
builder.Services.AddMediatorPipeline(typeof(CrearHotelCommand).Assembly);

// Adaptadores de infraestructura (DbContext + repositorio).
builder.Services.AddHotelesInfrastructure(builder.Configuration.GetConnectionString("hotelesdb"));

var app = builder.Build();

app.UseExceptionHandler();

// Autenticación/autorización (Story 6.1). Antes de los endpoints de negocio; /health y /alive quedan anónimos.
app.UseAuthentication();
app.UseAuthorization();

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
    .WithTags("Hoteles")
    .RequireAuthorization();

// CAP-1 · Editar hotel (AC-E2.2.1). El id de la ruta manda sobre el del cuerpo (evita discrepancias). El
// rowVersion del cuerpo arbitra la concurrencia optimista (409 si pierde la carrera; el handler transversal
// lo traduce). 404 si no existe o fue eliminado.
app.MapPut("/api/v1/hoteles/{id:guid}", async (Guid id, EditarHotelCommand comando, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(comando with { Id = id }, ct);
        return resultado.ToOkResult();
    })
    .WithName("EditarHotel")
    .WithTags("Hoteles")
    .RequireAuthorization();

// CAP-1 · Eliminar hotel — baja lógica (AC-E2.2.2). El rowVersion (cuerpo) arbitra la concurrencia. 204 al
// eliminar; 404 si no existe o ya estaba eliminado; 409 si pierde una carrera contra otra edición/baja.
app.MapDelete("/api/v1/hoteles/{id:guid}", async (Guid id, EliminarHotelCommand comando, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(comando with { Id = id }, ct);
        return resultado.ToNoContentResult();
    })
    .WithName("EliminarHotel")
    .WithTags("Hoteles")
    .RequireAuthorization();

// CAP-1 · Habilitar / deshabilitar hotel (AC-E2.3.1) — operaciones dedicadas del ciclo de vida. El estado
// objetivo lo fija la RUTA (no el cliente); el rowVersion (cuerpo) arbitra la concurrencia. 200 con el estado
// nuevo; 404 si no existe/eliminado; 409 en conflicto.
app.MapPost("/api/v1/hoteles/{id:guid}/habilitar", async (Guid id, CambiarEstadoHotelCommand comando, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(comando with { Id = id, EstadoObjetivo = EstadoHotel.Habilitado }, ct);
        return resultado.ToOkResult();
    })
    .WithName("HabilitarHotel")
    .WithTags("Hoteles")
    .RequireAuthorization();

app.MapPost("/api/v1/hoteles/{id:guid}/deshabilitar", async (Guid id, CambiarEstadoHotelCommand comando, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(comando with { Id = id, EstadoObjetivo = EstadoHotel.Deshabilitado }, ct);
        return resultado.ToOkResult();
    })
    .WithName("DeshabilitarHotel")
    .WithTags("Hoteles")
    .RequireAuthorization();

// CAP-1 · Habitaciones (FR-5/6/7). El hotel de la ruta manda; el rowVersion (cuerpo) arbitra la concurrencia.
app.MapPost("/api/v1/hoteles/{hotelId:guid}/habitaciones", async (Guid hotelId, CrearHabitacionCommand comando, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(comando with { HotelId = hotelId }, ct);
        return resultado.ToCreatedResult(dto => $"/api/v1/habitaciones/{dto.Id}");
    })
    .WithName("CrearHabitacion")
    .WithTags("Habitaciones")
    .RequireAuthorization();

app.MapPut("/api/v1/habitaciones/{id:guid}", async (Guid id, EditarHabitacionCommand comando, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(comando with { Id = id }, ct);
        return resultado.ToOkResult();
    })
    .WithName("EditarHabitacion")
    .WithTags("Habitaciones")
    .RequireAuthorization();

// Estado objetivo fijado por la RUTA (operaciones dedicadas); 200 con el estado nuevo, 404, 409.
app.MapPost("/api/v1/habitaciones/{id:guid}/habilitar", async (Guid id, CambiarEstadoHabitacionCommand comando, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(comando with { Id = id, EstadoObjetivo = EstadoHabitacion.Habilitada }, ct);
        return resultado.ToOkResult();
    })
    .WithName("HabilitarHabitacion")
    .WithTags("Habitaciones")
    .RequireAuthorization();

app.MapPost("/api/v1/habitaciones/{id:guid}/deshabilitar", async (Guid id, CambiarEstadoHabitacionCommand comando, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(comando with { Id = id, EstadoObjetivo = EstadoHabitacion.Deshabilitada }, ct);
        return resultado.ToOkResult();
    })
    .WithName("DeshabilitarHabitacion")
    .WithTags("Habitaciones")
    .RequireAuthorization();

app.Run();
