var builder = DistributedApplication.CreateBuilder(args);

// --- Infraestructura: una BD por servicio (ADR-001) + caché/idempotencia + broker de eventos ---
var sqlHoteles = builder.AddSqlServer("sql-hoteles").AddDatabase("db-hoteles");
var sqlReservas = builder.AddSqlServer("sql-reservas").AddDatabase("db-reservas");
var redis = builder.AddRedis("redis");
var rabbit = builder.AddRabbitMQ("rabbitmq");

// NOTA: el pub/sub por Dapr (sidecars) se añade en la Story 1.6b/E5; requiere Dapr CLI.
// La comunicación entre BC es solo por eventos (ADR-002); aquí se declara la topología base.

// --- Servicios de dominio + worker + gateway ---
var hoteles = builder.AddProject<Projects.Hoteles_Api>("hoteles")
    .WithReference(sqlHoteles)
    .WaitFor(sqlHoteles);

var reservas = builder.AddProject<Projects.Reservas_Api>("reservas")
    .WithReference(sqlReservas)
    .WaitFor(sqlReservas)
    .WithReference(redis)
    .WithReference(rabbit);

builder.AddProject<Projects.Notificaciones_Worker>("notificaciones")
    .WithReference(redis)
    .WithReference(rabbit);

// El Gateway es el único punto de entrada; enruta hacia los BC.
builder.AddProject<Projects.ApiGateway>("gateway")
    .WithReference(hoteles)
    .WithReference(reservas);

builder.Build().Run();
