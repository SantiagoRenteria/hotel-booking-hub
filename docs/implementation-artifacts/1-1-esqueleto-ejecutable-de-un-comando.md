# Story 1.1: Esqueleto ejecutable de un comando (walking skeleton)

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

Como **evaluador de la prueba**,
quiero **levantar todo el sistema con un solo comando y verificar que responde (incluido un salto asíncrono real)**,
para **revisar la solución sin instalar el SDK de .NET ni los workloads de Aspire, con la confianza de que la topología distribuida está cableada desde el primer commit**.

> **Rol de la historia:** es la **primera historia de implementación** (gate **E1a**). Entrega el esqueleto + telemetría + salto async + CI, NO lógica de negocio. El wiring del mediator es la Story 1.2 (spike) y el dominio empieza en 1.4. No implementes reglas de reserva aquí.

## Acceptance Criteria

1. **AC-E1.1.1 — Arranque reproducible.** Dado un checkout limpio sin SDK de .NET instalado, cuando ejecuto `docker compose up`, entonces los servicios (`ApiGateway`, `Hoteles.Api`, `Reservas.Api`, `Notificaciones.Worker`) alcanzan estado *healthy* y `GET /health` responde `200` en cada servicio.
2. **AC-E1.1.2 — Estructura y gobernanza desde el primer commit.** Existen 4 assemblies por BC (`.Domain`, `.Application`, `.Infrastructure`, `.Api`), `Directory.Packages.props` (CPM) y `Directory.Build.props` (`net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors` con `CA2016`); `NetArchTest` verifica que `Reservas.Domain` no referencia `Microsoft.EntityFrameworkCore`.
3. **AC-E1.1.3 — CI verde y smoke test de compose.** En un push a `develop`, el pipeline pasa `build` + `dotnet format` + `gitleaks`, y el smoke test (`docker compose up` + verificación de `/health`) pasa, detectando *drift* del compose a mano (ADR-007).
4. **AC-E1.1.4 — Salto asíncrono real cableado (no un standing skeleton).** Dado el esqueleto en marcha, cuando un servicio publica un evento de prueba por Dapr pub/sub, entonces `Notificaciones.Worker` lo consume (publish→consume verificado de punta a punta), probando que el componente Dapr, la suscripción y el sidecar están cableados desde el día uno.

## Tasks / Subtasks

- [ ] **Task 1 — Base de la solución + gobernanza de versiones (AC: 2)**
  - [ ] `dotnet new sln --name HotelBookingHub` en la raíz
  - [ ] Crear `Directory.Packages.props` (Central Package Management: `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`, una `<PackageVersion>` por paquete)
  - [ ] Crear `Directory.Build.props` con `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` y habilitar el analyzer `CA2016`
  - [ ] `.editorconfig`, `.gitignore` (.NET: bin/obj, appsettings.Development.json, secretos), `.dockerignore`
- [ ] **Task 2 — Piezas transversales de Aspire (AC: 1, 2)**
  - [ ] `dotnet new aspire-servicedefaults --name ServiceDefaults --output src/AppHost/ServiceDefaults`
  - [ ] `dotnet new aspire-apphost --name AppHost --output src/AppHost/AppHost` (NuGet-only vía `Aspire.AppHost.Sdk`; **no** `dotnet workload install aspire`)
  - [ ] `dotnet sln add` de ambos
- [ ] **Task 3 — Servicios de dominio + worker + gateway con sus 4 assemblies (AC: 2)**
  - [ ] `Hoteles`: `Hoteles.Api` (`dotnet new webapi --use-minimal-apis`) + bibliotecas `Hoteles.Application`, `Hoteles.Domain`, `Hoteles.Infrastructure`
  - [ ] `Reservas`: `Reservas.Api` (minimal API) + `Reservas.Application`, `Reservas.Domain`, `Reservas.Infrastructure`
  - [ ] `Notificaciones.Worker` (`dotnet new worker`)
  - [ ] `ApiGateway` (`dotnet new web` vacío — NO `webapi`) + paquete `Yarp.ReverseProxy`
  - [ ] `src/Comun/HotelBookingHub.Comun` (biblioteca de contratos transversales; carpetas `Resultados/ Mediador/ Behaviors/ Eventos/ Errores/ Primitivos/`, aún vacías o con stubs mínimos)
  - [ ] Wire de dependencias de capa: `Api → Application → Infrastructure → Domain` (referencias hacia adentro)
  - [ ] `ProjectReference` por CADA ejecutable desde `AppHost.csproj` (habilita `Projects.*` del source-generator)
  - [ ] Limpieza del scaffold: borrar `WeatherForecast.cs` y el endpoint `/weatherforecast` de cada `Program.cs`; revisar `Properties/launchSettings.json`
- [ ] **Task 4 — Salud, telemetría y recursos declarados (AC: 1)**
  - [ ] Cada servicio llama `builder.AddServiceDefaults()` (OTel + health checks + resiliencia) y `app.MapDefaultEndpoints()` (`/health`, `/alive`)
  - [ ] `AppHost` declara recursos: SQL Server ×2 (db_hoteles, db_reservas), Redis, RabbitMQ (broker), sidecars Dapr, y el dashboard OTel
- [ ] **Task 5 — Salto asíncrono real por Dapr pub/sub (AC: 4)**
  - [ ] Componente Dapr pub/sub sobre RabbitMQ en `deploy/dapr/` (local)
  - [ ] Endpoint temporal de prueba (p. ej. `POST /_smoke/ping` en `Reservas.Api`) que publica un evento de prueba al topic vía Dapr
  - [ ] Suscripción en `Notificaciones.Worker` que consume ese topic y registra/marca la recepción
  - [ ] Test de integración que verifica publish→consume de punta a punta (atraviesa el borde de proceso). *Marcar el endpoint de prueba como temporal/desechable — se retira cuando llegue el evento real de reserva (Story 1.6/5.1a).*
- [ ] **Task 6 — `docker-compose` a mano + smoke test (AC: 1, 3)**
  - [ ] `deploy/docker-compose.yml` reproducible: servicios + SQL ×2 + Redis + RabbitMQ + sidecars Dapr + dashboard de Aspire standalone (contenedores con `USER` no root, tags específicos no `:latest`, `HEALTHCHECK`)
  - [ ] Verificar `docker compose up` en frío → `/health` `200` en cada servicio sin SDK instalado
- [ ] **Task 7 — CI en GitHub Actions (AC: 3)**
  - [ ] `.github/workflows/ci.yml`: `dotnet build` + `dotnet format --verify-no-changes` + `gitleaks` + smoke test de compose (`docker compose up` + curl `/health`)
  - [ ] Correr el `NetArchTest` de la Task 3 dentro del stage de test
- [ ] **Task 8 — Commit + push a `develop`** (cadencia obligatoria por cambio cerrado; autor Santiago Renteria, sin trailers de coautoría/IA)

## Dev Notes

### Stack y versiones (fuente: `architecture.md#Starter Template Evaluation`, verificadas 2026-07-08)

- **.NET 10 (LTS)** — GA 11-nov-2025, LTS hasta nov-2028. Satisface el ".NET 8 o superior" del enunciado.
- **Aspire 13** (`Aspire.ProjectTemplates` 13.4.x) — requiere SDK .NET 10 para el AppHost en C#. **NuGet-only vía `Aspire.AppHost.Sdk`**; el modelo `dotnet workload install aspire` está **deprecado y NO se usa**.
- **OpenAPI nativo** de .NET 10 (`Microsoft.AspNetCore.OpenApi`, **sin Swashbuckle**); UI vía Scalar (ADR-011) — el cableado de Scalar puede quedar para la historia que exponga endpoints reales; aquí basta dejar OpenAPI nativo listo.
- **YARP** (`Yarp.ReverseProxy`) en el Gateway; **EF Core 10**, `StackExchange.Redis`, `Dapr.Client` — declarados en CPM aunque su uso llegue en historias posteriores.
- **Versiones exactas de cada paquete:** re-verificar la vigente al ejecutar y fijarla en `Directory.Packages.props` (una sola versión por paquete, sin drift entre servicios).

### Comandos de inicialización (copiar de `architecture.md#Initialization Command`)

El bloque de comandos exacto (`dotnet new sln`, `aspire-servicedefaults`, `aspire-apphost`, los `webapi --use-minimal-apis`, el `worker`, el `web` + YARP, y los `dotnet add reference` desde el AppHost) está en [architecture.md](../planning-artifacts/architecture.md) sección *Selected Starter → Initialization Command*. Seguirlo al pie de la letra, incluida la limpieza del scaffold `WeatherForecast`.

### Decisiones de arquitectura que esta historia materializa (guardarraíles)

- **ADR-015 (sin `aspire-starter`):** se adopta solo `aspire-apphost` + `aspire-servicedefaults`; NO scaffoldear el sample Blazor/API. La ausencia del sample es criterio, no omisión.
- **ADR-007 (`docker-compose` a mano):** el compose se mantiene manual (NO se genera con `Aspire.Hosting.Docker`) y se blinda con el smoke test de CI contra *drift*.
- **Estructura de 4 assemblies por BC** (frontera de ensamblado = evidencia visible de Clean Architecture). Dependencias hacia adentro `Domain ← Application ← Infrastructure ← Api`. Fuente: [architecture.md](../planning-artifacts/architecture.md) *Project Structure & Boundaries*.
- **`Comun` = solo contratos transversales.** Regla de admisión: un tipo entra a `Comun` solo si es convención de infraestructura transversal (cómo se comunican los servicios) y cambiarlo requeriría coordinar ambos BC. **Ningún tipo de dominio** de Hoteles/Reservas va en `Comun`.
- **Salto async desde el día uno:** exigido por el party-mode (Winston) para no descubrir tarde que el pub/sub Dapr no estaba cableado. Es enabler compartido por Stories 3.1, E4 y E5.

### Convención tri-idioma (obligatoria — fuente `stack-and-conventions.md` / `AGENTS.md` futuro)

> **¿Qué es? → español sin tilde. ¿Cómo se implementa? → inglés. ¿Qué le digo al humano? → español con tilde.**

- Identificadores de dominio: español **sin tilde** (`Habitacion`, `Reserva`, `NochesHabitacion`).
- Sufijos de patrón en inglés (lista cerrada): `Command, Query, Handler, Repository, Service, Validator, Behavior, Dto, Request, Response, Factory, Exception, Api, Worker, Middleware`.
- Mensajes de negocio / comentarios: español **con tildes** (`"La habitación no está disponible"`).
- Namespace raíz `HotelBookingHub` (marca, en inglés); dominio en español dentro de cada servicio.
- Tablas SQL: PascalCase español plural (`Reservas`, `NochesHabitacion`, `OutboxMessages`).

### Seguridad y calidad (no violar — fuente `security-and-quality.md`)

- **Cero secretos en el repo.** `appsettings.json` solo valores no sensibles (placeholders `""`); secretos vía Dapr Secrets (local). gitleaks en CI debe quedar en verde.
- Contenedores: `USER` no root, tags específicos (no `:latest`), multi-stage build, `.dockerignore`, `HEALTHCHECK`.
- `<Nullable>enable</Nullable>`, `sealed` por defecto, `CancellationToken` propagado (analyzer `CA2016` en `TreatWarningsAsErrors`).

### Git (cadencia obligatoria de esta sesión)

- Commit **y push a `develop`** por cada cambio cerrado (no esperar a un commit grande).
- Conventional Commits en español (`feat:`, `chore:`, `ci:`…). Autor **Santiago Renteria**; **sin** trailers de coautoría ni firmas de herramientas de IA.

### Testing (fuente `delivery-and-testing.md` / `architecture.md#Estrategia de tests`)

- Proyectos de test desde ya: `Reservas.UnitTests` (incluye el `NetArchTest`), y el andamiaje de `Reservas.IntegrationTests` para el test de Task 5 (Testcontainers llega en 1.5; el salto async de 1.1 puede probarse con el fake/loopback de Dapr o el broker del compose).
- `TestKit` (class lib con fixtures/builders/collections) puede esbozarse aquí y crecer en 1.5/1.6.
- El `NetArchTest` mínimo de esta historia: `Reservas.Domain` no referencia `Microsoft.EntityFrameworkCore`.

### Anti-patrones a evitar en esta historia

- Meter lógica de negocio en el Gateway o en `Comun`.
- Dejar el `WeatherForecast` del scaffold.
- Usar `dotnet workload install aspire` (deprecado) en vez del modelo NuGet.
- Generar el `docker-compose` desde `Aspire.Hosting.Docker` (va a mano, ADR-007).
- Hardcodear cualquier secreto/connection string.
- Construir el pipeline del mediator aquí (es Story 1.2/1.4).

### Project Structure Notes

Árbol objetivo (fuente: [architecture.md](../planning-artifacts/architecture.md) *Árbol de proyecto completo*). Esta historia crea el esqueleto de:

```
hotel-booking-hub/
├── HotelBookingHub.sln
├── Directory.Build.props · Directory.Packages.props · .editorconfig · .gitignore · .dockerignore
├── .github/workflows/ci.yml
├── src/
│   ├── AppHost/{AppHost, ServiceDefaults}
│   ├── ApiGateway/                      # YARP (dotnet new web)
│   ├── Comun/HotelBookingHub.Comun/     # solo contratos transversales
│   └── Servicios/
│       ├── Hoteles/{Hoteles.Domain,.Application,.Infrastructure,.Api}
│       ├── Reservas/{Reservas.Domain,.Application,.Infrastructure,.Api}
│       └── Notificaciones/Notificaciones.Worker/
├── tests/
│   ├── TestKit/  (esbozo)
│   ├── Reservas.UnitTests/  (NetArchTest)
│   └── Reservas.IntegrationTests/  (andamiaje para el salto async)
└── deploy/{docker-compose.yml, dapr/}
```

- **Variación conocida:** `postman/`, `terraform/`, migraciones EF y las tablas se crean en las historias que las necesitan (1.5 crea el schema; E8 el Terraform). NO crear tablas ni migraciones aquí (principio "crea lo que la historia necesita").

### References

- [epics.md — Story 1.1](../planning-artifacts/epics.md) (AC-E1.1.1…4) y *Convención de historias y criterios de aceptación*.
- [architecture.md — Starter Template Evaluation](../planning-artifacts/architecture.md) (comandos de init, ADR-015, versiones verificadas).
- [architecture.md — Project Structure & Boundaries](../planning-artifacts/architecture.md) (árbol, 4 assemblies, regla de admisión de `Comun`).
- [architecture.md — Infrastructure & Deployment](../planning-artifacts/architecture.md) (Aspire, docker-compose ADR-007, CI).
- [stack-and-conventions.md](../specs/spec-hotel-booking-hub/stack-and-conventions.md) (convención tri-idioma, CPM, `DateTimeOffset`).
- [security-and-quality.md](../specs/spec-hotel-booking-hub/security-and-quality.md) (cero secretos, contenedores, calidad C#).
- [decisions-adr.md](../specs/spec-hotel-booking-hub/decisions-adr.md) (ADR-007, ADR-011, ADR-015).

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
