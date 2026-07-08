# hotel-booking-hub

Sistema de **gestión y reserva de hoteles** para una agencia de viajes — back end distribuido en .NET 10.
Prueba técnica Back End Developer · UltraGroup (Tech, Travel & Loyalty).

> **Estado:** 🚧 Fase 2 — scaffold del repositorio. Aún sin código de aplicación.
> La ingeniería completa (requisitos, arquitectura, ADRs, decisiones) está en **[docs/DOCUMENTO-BASE.md](docs/DOCUMENTO-BASE.md)**.

---

## Visión general

Dos microservicios alineados a *Bounded Contexts* (DDD) + API Gateway + worker de notificaciones, comunicados por eventos.

| Componente | Rol |
|------------|-----|
| `ApiGateway` (YARP) | Entrada única: JWT, rate limiting, HTTPS |
| `Hoteles.Api` | BC Hoteles — catálogo de hoteles y habitaciones |
| `Reservas.Api` | BC Reservas — búsqueda, reserva y anti-overbooking |
| `Notificaciones.Worker` | Consume `ReservaConfirmada` → correo a huésped y agente |

**Stack:** C# / .NET 10 · SQL Server · Redis · Dapr (pub/sub + secrets) · RabbitMQ (local) / Azure Service Bus (nube) · .NET Aspire · OpenTelemetry · Terraform + Azure Container Apps.

## Arquitectura

- **Clean Architecture** por servicio (`Domain ← Application ← Infrastructure ← API`).
- **CQRS** con mediator propio + pipeline de behaviors.
- **Anti-overbooking** garantizado por el motor (slots de inventario `NochesHabitacion` + `UNIQUE` + `SERIALIZABLE`).
- **Transactional Outbox** + idempotencia (Redis) para no perder ni duplicar eventos.
- **Seguridad** mapeada a OWASP Top 10; readiness PCI DSS / ISO 27001.

Diagramas C4 y detalle completo en [docs/DOCUMENTO-BASE.md](docs/DOCUMENTO-BASE.md).

## Estructura del repositorio

```
src/         Gateway, Servicios/{Hoteles,Reservas,Notificaciones}, Comun (shared kernel), AppHost (Aspire)
tests/       Unit + Integration (Testcontainers.MsSql)
deploy/      docker-compose, Dapr components, Terraform (Azure)
docs/        PRD, architecture, ADRs
postman/     colección + entorno (Newman)
```

## Cómo ejecutar

> Pendiente (Fase 1). Habrá dos rutas: `dotnet run` sobre el AppHost de Aspire (desarrollo) y `docker compose up` (reproducible, sin instalar el SDK).

## Convenciones

- **Idioma del código:** dominio en español sin tildes (`Habitacion`, `Reserva`); sufijos de patrón en inglés (`Command`, `Repository`).
- **Ramas:** `main` (estable) · `develop` (integración) · `feature/*`.
- **Commits:** Conventional Commits (`feat:`, `fix:`, `docs:`, `test:`...).

## Licencia

Uso educativo / evaluación técnica.
