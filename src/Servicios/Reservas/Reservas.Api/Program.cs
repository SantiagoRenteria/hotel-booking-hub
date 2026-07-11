using System.Text.Json.Serialization;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Web;
using HotelBookingHub.Comun.Web.Seguridad;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reservas.Api;
using Reservas.Application.Abstracciones;
using Reservas.Application.Reservas.BuscarDisponibilidad;
using Reservas.Application.Reservas.CancelarEnUnPaso;
using Reservas.Application.Reservas.CrearReserva;
using Reservas.Application.Reservas.ListarCancelacionesPendientes;
using Reservas.Application.Reservas.ListarReservasDelAgente;
using Reservas.Application.Reservas.ObtenerReservaDetalle;
using Reservas.Application.Reservas.ResolverCancelacion;
using Reservas.Application.Reservas.SolicitarCancelacion;
using Reservas.Domain.Servicios;
using Reservas.Infrastructure;
using Reservas.Infrastructure.Persistencia;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// OpenAPI nativo de .NET 10 (sin Swashbuckle). La UI Scalar se añade cuando haya endpoints de negocio.
builder.Services.AddOpenApi();

// Enums por NOMBRE en el JSON (entrada y salida): el cuerpo acepta "iniciador":"Viajero"/"Agente" —simétrico
// con el evento, que serializa el nombre— en vez de forzar el ordinal numérico. Un nombre desconocido lo atrapa
// el validator (IsInEnum → 400).
builder.Services.ConfigureHttpJsonOptions(opciones =>
    opciones.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Autenticación JWT (Story 6.1, defensa en profundidad): el servicio valida el token además del Gateway
// (no confía en un header inyectado como auth). La identidad/rol del claim se usan en 6.2 (RBAC) y 6.3 (aislamiento).
builder.Services.AddAutenticacionJwt(builder.Configuration);

// Autorización por rol (Story 6.2): policies SoloAgente / AgenteOViajero resueltas server-side (403 sin permiso).
builder.Services.AddAutorizacionPorRol();

// Excepciones de negocio → Problem Details RFC 7807 (handler transversal en Comun.Web; overbooking → 409).
builder.Services.AddManejoExcepcionesNegocio();

// Pipeline del mediator (Logging → Validation → Handler) + validators, por scan del assembly de Application.
builder.Services.AddMediatorPipeline(typeof(CrearReservaCommand).Assembly);

// Domain services puros, sin estado.
builder.Services.AddSingleton<CalculadorPrecio>();
builder.Services.AddSingleton<CalculadorPenalidad>(); // penalidad de cancelación sugerida (Story 4.1)

// Reloj inyectable: el handler resuelve la fecha de solicitud para congelar la penalidad de forma determinista.
builder.Services.AddSingleton(TimeProvider.System);

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

// Identidad del agente resuelta server-side desde el claim 'email' del token JWT validado (Story 6.3, cierra
// el puente X-Agente de 3.3). El filtro de aislamiento vive en los handlers/queries (dependen de la interfaz).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IContextoAgente, ClaimContextoAgente>();

var app = builder.Build();

// Migraciones al arranque SOLO si se solicita (`AplicarMigraciones=true`, que activa docker-compose para el
// data-plane local de un comando). Desactivado en tests (cadena falsa) y en la nube (migraciones por CD).
// Story T.1 (AC-ET.1.5).
if (app.Configuration.GetValue<bool>("AplicarMigraciones"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ReservasDbContext>();
    // En docker-compose el servicio arranca antes de que SQL Server acepte conexiones → reintenta hasta ~60s.
    for (var intento = 1; ; intento++)
    {
        try { db.Database.Migrate(); break; }
        catch when (intento < 30) { await Task.Delay(TimeSpan.FromSeconds(2)); }
    }
}

app.UseExceptionHandler();

// Autenticación/autorización (Story 6.1). Va antes de los endpoints de negocio; /health y /alive quedan anónimos.
app.UseAuthentication();
app.UseAuthorization();

// OpenAPI + UI Scalar (ADR-011): en Development, y en cualquier entorno si ExponerOpenApi=true (el docker-compose
// lo activa para que el evaluador lo alcance; en Azure/ACA NO se activa → no se expone la superficie de la API en
// la nube real). Scalar sirve la UI en /scalar y lee la spec de /openapi/v1.json.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("ExponerOpenApi"))
{
    app.MapOpenApi();
    app.MapScalarApiReference(opciones => opciones.WithTitle("Reservas.Api — hotel-booking-hub"));
}

// HTTPS enforcement es responsabilidad del API Gateway (borde único); los servicios corren HTTP tras él.

// /health y /alive (Story 1.1).
app.MapDefaultEndpoints();

// CAP-5 · Crear-confirmar reserva (FR-9/10/11) con idempotencia opcional por header `Idempotency-Key` (Story 1.7,
// DOCUMENTO-BASE §8.5): un reintento del cliente con la misma clave no crea una segunda reserva. Mapeo Result→HTTP
// centralizado en el manejador (201 al crear, 200 en replay, 422 si la clave se reutiliza con otro cuerpo).
app.MapPost("/api/v1/reservas", (
        CrearReservaCommand comando,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ISender sender,
        IAlmacenIdempotenciaReserva idempotencia,
        IContextoAgente contexto,
        CancellationToken ct) =>
        CrearReservaIdempotente.ManejarAsync(comando, idempotencyKey, sender, idempotencia, contexto, ct))
    .WithName("CrearReserva")
    .WithTags("Reservas")
    .Produces<Reservas.Application.Reservas.CrearReserva.ReservaResponseDto>(StatusCodes.Status201Created)
    .Produces<Reservas.Application.Reservas.CrearReserva.ReservaResponseDto>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
    .RequireAuthorization(PoliticasAutorizacion.AgenteOViajero);

// CAP · Solicitar cancelación con política sugerida (Story 4.1). Transición Confirmada→CancelacionSolicitada:
// guards de dominio (no elegible/duplicada/estancia iniciada) → 409; la respuesta incluye la penalidad SUGERIDA
// congelada (no se cobra). Result→HTTP centralizado.
app.MapPost("/api/v1/reservas/{id:guid}/solicitud-cancelacion", async (
        Guid id, SolicitarCancelacionRequest cuerpo, ISender sender, CancellationToken ct) =>
    {
        var comando = new SolicitarCancelacionCommand(id, cuerpo.CategoriaMotivo, cuerpo.DetalleMotivo, cuerpo.Iniciador);
        var resultado = await sender.Send(comando, ct);
        return resultado.ToOkResult();
    })
    .WithName("SolicitarCancelacion")
    .WithTags("Reservas")
    .RequireAuthorization(PoliticasAutorizacion.AgenteOViajero);

// CAP · Resolver cancelación (Story 4.2): el agente aprueba (aplicando/condonando la penalidad congelada) o
// rechaza (con motivo). Aprobar libera el inventario. Identidad del agente server-side (aislamiento → 403 ajeno);
// guards de estado → 409; concurrencia optimista → 409. Result→HTTP centralizado.
app.MapPost("/api/v1/reservas/{id:guid}/cancelacion/resolucion", async (
        Guid id, ResolverCancelacionRequest cuerpo, ISender sender, CancellationToken ct) =>
    {
        var comando = new ResolverCancelacionCommand(id, cuerpo.Decision, cuerpo.MotivoRechazo);
        var resultado = await sender.Send(comando, ct);
        return resultado.ToOkResult();
    })
    .WithName("ResolverCancelacion")
    .WithTags("Reservas")
    .RequireAuthorization(PoliticasAutorizacion.SoloAgente);

// CAP · Atajo de cancelación en un paso (Story 4.3): solicita + resuelve en una tx, con auditoría de AMBOS
// eventos. Identidad del agente server-side; Result→HTTP.
app.MapPost("/api/v1/reservas/{id:guid}/cancelaciones/atajo", async (
        Guid id, CancelarEnUnPasoRequest cuerpo, ISender sender, CancellationToken ct) =>
    {
        var comando = new CancelarEnUnPasoCommand(
            id, cuerpo.CategoriaMotivo, cuerpo.DetalleMotivo, cuerpo.Iniciador, cuerpo.Decision, cuerpo.MotivoRechazo);
        var resultado = await sender.Send(comando, ct);
        return resultado.ToOkResult();
    })
    .WithName("CancelarEnUnPaso")
    .WithTags("Reservas")
    .RequireAuthorization(PoliticasAutorizacion.SoloAgente);

// CAP · Visibilidad de cancelaciones pendientes con antigüedad (Story 4.3, AC-E4.3.3). Aislada por agente.
app.MapGet("/api/v1/reservas/cancelaciones-pendientes", async (ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(new ListarCancelacionesPendientesQuery(), ct);
        return resultado.ToOkResult();
    })
    .WithName("ListarCancelacionesPendientes")
    .WithTags("Reservas")
    .RequireAuthorization(PoliticasAutorizacion.SoloAgente);

// CAP-4 · Búsqueda de habitaciones disponibles (FR-8). Query CQRS servida desde la proyección + caché Redis.
app.MapGet("/api/v1/habitaciones/disponibles", async (
        string ciudad, DateOnly entrada, DateOnly salida, int huespedes, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(new BuscarDisponibilidadQuery(ciudad, entrada, salida, huespedes), ct);
        return resultado.ToOkResult();
    })
    .WithName("BuscarDisponibilidad")
    .WithTags("Habitaciones")
    .RequireAuthorization(PoliticasAutorizacion.AgenteOViajero);

// CAP-4 · Listado de reservas del agente (FR-12) y su detalle. Aislamiento server-side por identidad del agente
// (IContextoAgente); el cliente NO elige a qué agente ve. Result→HTTP centralizado.
app.MapGet("/api/v1/reservas", async (ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(new ListarReservasDelAgenteQuery(), ct);
        return resultado.ToOkResult();
    })
    .WithName("ListarReservasDelAgente")
    .WithTags("Reservas")
    .RequireAuthorization(PoliticasAutorizacion.SoloAgente);

app.MapGet("/api/v1/reservas/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
    {
        var resultado = await sender.Send(new ObtenerReservaDetalleQuery(id), ct);
        return resultado.ToOkResult();
    })
    .WithName("ObtenerReservaDetalle")
    .WithTags("Reservas")
    .RequireAuthorization(PoliticasAutorizacion.SoloAgente);

app.Run();

// Expone la clase Program para WebApplicationFactory (tests funcionales de RBAC/aislamiento, Story 6.2/6.3).
public partial class Program;
