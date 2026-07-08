# Stack, restricciones técnicas y convenciones de código

Compañero de [SPEC.md](SPEC.md). Detalla las constraints de stack, idioma y reglas obligatorias.

## Stack y decisiones técnicas

| Aspecto | Decisión | Justificación breve |
|---------|----------|---------------------|
| Lenguaje / Framework | **C# · .NET 10** (Minimal API, sin MVC) | Requisito del enunciado; satisface ".NET 8 o superior"; features modernas |
| Motor transaccional | **SQL Server** (una BD por microservicio) | Motor del stack de UltraGroup; consistencia fuerte para reservas |
| Caché / state / idempotencia | **Redis** | Caché de disponibilidad, store de idempotencia (inbox) y Dapr state store |
| ORM | **EF Core 10** (code-first + migraciones) | Tracking de entidades DDD, provider SQL Server |
| API | **REST** con **OpenAPI**; UI con **Scalar** | OpenAPI cumple "OpenAPI/Swagger" |
| Mensajería / runtime distribuido | **Dapr** (pub/sub + secrets) | Cloud-agnostic, nativo en Azure Container Apps |
| Broker | **RabbitMQ** (local) / **Azure Service Bus** (nube) | Intercambiable por *component* Dapr, sin tocar código |
| Validación | **FluentValidation** | Validación declarativa (anti-inyección, OWASP A03) |
| Testing | **xUnit** + EF Core InMemory + **Testcontainers.MsSql** | TDD; integración con SQL Server real en contenedor |
| Pruebas de API | **Postman + Newman** | Colección versionada, ejecutada en CI |
| Orquestación local | **.NET Aspire** | Dev-loop + dashboard OpenTelemetry + wiring automático |
| Reproducibilidad | **docker-compose** | Requisito de entrega; el evaluador no necesita Aspire |
| Nube | **Azure Container Apps** + **Terraform** | IaC + Dapr gestionado + escalado por réplicas (KEDA) |

> **MongoDB** aparece en el stack de la empresa; se documenta como evolución (read model NoSQL vía CQRS) pero **no se implementa** en esta entrega (ver Non-goals del SPEC y ADR-013).

## Idioma del código

- **Dominio en español, identificadores SIN tildes** (evita problemas de codificación): `Habitacion`, `Huesped`, `Reserva`, `TipoHabitacion`, `NocheHabitacion`.
- **Sufijos de patrón / infraestructura en inglés** (convención .NET): `Command`, `Query`, `Handler`, `Repository`, `Service`, `Dto`, `Factory`, `Validator`, `Behavior`, `Api`, `Worker`, `Middleware`. Ej.: `CrearReservaCommand`, `IReservaRepository`.
- **Patrones que son sustantivos de dominio pueden ir en español:** `EspecificacionHabitacionDisponible`, `EstrategiaPrecioTemporada`.
- **Palabras reservadas y APIs del framework** siempre en inglés: `class`, `async`, `IEnumerable`, `DateTimeOffset`.
- **Comentarios, mensajes al usuario y textos de negocio:** español CON tildes correctas.

## Reglas clave (obligatorias)

- **Dependencias apuntan hacia adentro** (Clean Architecture); lógica de negocio solo en Domain/Application. El dominio define **puertos**; infraestructura provee **adaptadores**.
- **Result Pattern** (`Result<T>`) para flujos de negocio esperados; **excepciones solo** para errores inesperados/infraestructura.
- **`DateTimeOffset`** siempre (nunca `DateTime`); `DateOnly` para fechas de estancia.
- **UUID v7** en todas las PKs (`Guid.CreateVersion7()`).
- **Concurrencia optimista** con columna `rowversion` (SQL Server) → `rowVersion` en DTOs; conflicto → 409.
- **Minimal API** (no MVC), **versionado por URL** `/api/v1/`.
- **Problem Details RFC 7807** en todos los errores.
- **RBAC** en cada endpoint (`RequireAuthorization` + policies).
- Entidades DDD con **factory methods** y setters privados.

## Naming

| Elemento | Convención | Ejemplo |
|----------|-----------|---------|
| Entidades / aggregates | PascalCase español, singular | `Reserva`, `Habitacion` |
| Commands | `{Accion}{Entidad}Command` | `CrearReservaCommand` |
| Queries | `{Accion}{Entidad}Query` | `BuscarHabitacionesDisponiblesQuery` |
| Handlers | `{Comando/Query}Handler` | `CrearReservaCommandHandler` |
| DTOs | `{Entidad}{Tipo}Dto` | `ReservaResponseDto` |
| Interfaces (puertos) | `I` + PascalCase | `IReservaRepository`, `IServicioCorreo` |
| Domain events | PascalCase español, participio | `ReservaConfirmada` |
| Tablas (SQL Server) | PascalCase español, plural | `Reservas`, `NochesHabitacion` |

## Estructura del repositorio

```
hotel-booking-hub/
├── .github/workflows/            # CI: build, test, newman, gitleaks
├── docs/
│   ├── prd.md
│   ├── architecture.md           # C4 + decisiones
│   └── adr/                       # un archivo por ADR
├── src/
│   ├── ApiGateway/                        # YARP (auth, rate limit, HTTPS)
│   ├── Servicios/
│   │   ├── Hoteles/{Hoteles.Api, .Application, .Domain, .Infrastructure}
│   │   ├── Reservas/{Reservas.Api, .Application, .Domain, .Infrastructure}
│   │   └── Notificaciones/Notificaciones.Worker/   # consumidor Dapr + correo
│   ├── Comun/HotelBookingHub.Comun/       # shared kernel: Result, mediator, behaviors, ProblemDetails
│   └── AppHost/                           # .NET Aspire AppHost + ServiceDefaults
├── tests/
│   ├── Hoteles.UnitTests/  ·  Hoteles.IntegrationTests/
│   └── Reservas.UnitTests/ ·  Reservas.IntegrationTests/
├── deploy/{docker-compose.yml, dapr/, terraform/}
├── postman/                       # colección + entorno (Newman)
└── .gitignore · .editorconfig · HotelBookingHub.sln · README.md
```

> Namespace raíz `HotelBookingHub` (marca del producto, en inglés); dominio dentro de cada servicio en español. Sufijos de capa (`Api`, `Application`, `Domain`, `Infrastructure`, `Worker`) son convención.

## Convenciones de Git

- **Ramas (GitFlow ligero):** `main` (estable, protegida), `develop` (integración), `feature/*` (una por historia), `release/*` y `hotfix/*` opcionales. Flujo: `feature/*` → PR → `develop`; al cerrar hito, `develop` → `main` (tag).
- **Commits — Conventional Commits:** `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`, `ci:` + descripción en español. Ej.: `feat(reservas): agrega anti-overbooking con slots de inventario`.
- **Cadencia (obligatorio):** cada cambio se **commitea y se hace push a `develop`** apenas queda cerrado, sin acumular commits grandes. Objetivo: aislar rápido cualquier error y mantener el historial trazable paso a paso. Durante la construcción se trabaja directo sobre `develop`; las ramas `feature/*` y los PR quedan como opción para hitos mayores.
- **Autoría (obligatorio):** todos los commits con autor **Santiago Renteria `<santiagorenteriahurtado@gmail.com>`**. Sin trailers de coautoría ni firmas de IA.
- **Higiene:** `.gitignore` .NET; `.editorconfig`; pre-commit con `gitleaks` + `dotnet format`.
