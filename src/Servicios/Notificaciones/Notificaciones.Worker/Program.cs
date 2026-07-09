using Notificaciones.Worker;
using Notificaciones.Worker.Notificaciones;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry + health checks + service discovery + resiliencia.
builder.AddServiceDefaults();

// Latido de fondo del worker.
builder.Services.AddHostedService<Worker>();

// Notificaciones (Story 5.1a): sink de Fase 1 (consola/MailHog) + consumidor de ReservaConfirmada.
// La suscripción al transporte real (Dapr pub/sub) sigue diferida como en el resto del sistema
// (PublicadorEventosLog es placeholder); el consumidor se invoca con el envelope (patrón de 3.1).
builder.Services.AddSingleton<INotificador, NotificadorConsola>();

// Inbox de idempotencia del consumidor (Story 5.1b): dedup del efecto por (MessageId, version, destinatario).
// Redis (SETNX+TTL) si está configurado —la única dedup válida entre instancias del worker—; si no, fallback en
// memoria para desarrollo local sin orquestador (misma política "Redis-si-configurado" de la caché de 3.2).
var cadenaRedis = builder.Configuration.GetConnectionString("redis");
if (!string.IsNullOrWhiteSpace(cadenaRedis))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(cadenaRedis));
    builder.Services.AddSingleton(new OpcionesInbox());
    builder.Services.AddSingleton<IInboxIdempotencia, InboxIdempotenciaRedis>();
}
else
{
    builder.Services.AddSingleton<IInboxIdempotencia, InboxIdempotenciaEnMemoria>();
}

builder.Services.AddSingleton<ConsumidorReservaConfirmada>();
builder.Services.AddSingleton<IProcesadorEvento>(sp => sp.GetRequiredService<ConsumidorReservaConfirmada>());

// Consumidor de SolicitudCancelacionRegistrada.v1 (Story 5.2): acuse al viajero (penalidad estimada) + aviso al
// agente. Reutiliza INotificador + inbox idempotente. El enrutamiento por tipo de evento llega con el transporte.
builder.Services.AddSingleton<ConsumidorSolicitudCancelacion>();

// Tope de intentos + dead-letter para el mensaje-veneno (Story 5.1b Task 4): un fallo permanente no debe
// re-reclamarse sin cota ni bloquear el stream. Contador en memoria (fallback local) + sink de dead-letter al log.
builder.Services.AddSingleton<IContadorReintentos, ContadorReintentosEnMemoria>();
builder.Services.AddSingleton<IColaDeadLetter, ColaDeadLetterLog>();
builder.Services.AddSingleton(new OpcionesDespachador());
builder.Services.AddSingleton<DespachadorNotificaciones>();

var app = builder.Build();

// /health y /alive del worker (Story 1.1).
app.MapDefaultEndpoints();

app.Run();
