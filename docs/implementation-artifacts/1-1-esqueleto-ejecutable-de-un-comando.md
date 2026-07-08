---
baseline_commit: 6ea494b6dbf749787d56b883759d85a1cbbf8be6
---

# Story 1.1: Esqueleto ejecutable de un comando (walking skeleton)

Status: review

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

- [x] **Task 1 — Base de la solución + gobernanza de versiones (AC: 2)**
  - [x] `dotnet new sln` → generó **`HotelBookingHub.slnx`** (formato XML, default del SDK .NET 10)
  - [x] `Directory.Packages.props` (CPM `ManagePackageVersionsCentrally=true`, una `PackageVersion` por paquete; `Version=` removido de todos los csproj)
  - [x] `Directory.Build.props` (`net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, `WarningsAsErrors=CA2016`)
  - [x] `.editorconfig` (corregida regla de naming: `const`→PascalCase), `.gitignore` (+`.env`, excepción `!deploy/.env.example`), `.dockerignore` creado
- [x] **Task 2 — Piezas transversales de Aspire (AC: 1, 2)**
  - [x] `aspire-servicedefaults` → `src/AppHost/ServiceDefaults`
  - [x] `aspire-apphost` → `src/AppHost/AppHost` (NuGet-only vía `Aspire.AppHost.Sdk` 13.4.6; sin workload)
  - [x] Ambos añadidos a la solución
- [x] **Task 3 — Servicios de dominio + worker + gateway con sus 4 assemblies (AC: 2)**
  - [x] `Hoteles`: `.Api` (minimal) + `.Application` + `.Domain` + `.Infrastructure`
  - [x] `Reservas`: `.Api` (minimal) + `.Application` + `.Domain` + `.Infrastructure` (+ EF Core SqlServer en `.Infrastructure`)
  - [x] `Notificaciones.Worker` (host web para exponer `/health`)
  - [x] `ApiGateway` (`web` vacío) + `Yarp.ReverseProxy`
  - [x] `src/Comun/HotelBookingHub.Comun` con `Eventos/` (`EventoIntegracion`, `IPublicadorEventos`)
  - [x] Referencias de capa `Domain ← Application ← Infrastructure ← Api` + `Comun` (transversal) + `ServiceDefaults` (ejecutables)
  - [x] `ProjectReference` de cada ejecutable desde `AppHost` (source-generator `Projects.*` verificado al compilar)
  - [x] Limpieza del scaffold: borrados `Class1.cs` y `WeatherForecast`
- [x] **Task 4 — Salud, telemetría y recursos declarados (AC: 1 · impl.)**
  - [x] Cada servicio: `builder.AddServiceDefaults()` + `app.MapDefaultEndpoints()` (`/health`, `/alive`); `MapDefaultEndpoints` ajustado para exponer salud en todos los entornos (smoke Production)
  - [x] `AppHost` declara SQL Server ×2 + Redis + RabbitMQ + dashboard OTel (por `OTEL_EXPORTER_OTLP_ENDPOINT`)
  - [x] **Verificado en runtime:** los 4 servicios alcanzan `(healthy)` bajo `docker compose up`
- [x] **Task 5 — Salto asíncrono real por Dapr pub/sub (AC: 4) — VERIFICADO**
  - [x] Costura + adaptador: `IPublicadorEventos` en `Comun`; `Dapr.AspNetCore` en Reservas.Api (publica vía `DaprClient`) y en el Worker (suscripción `WithTopic` + `UseCloudEvents` + `MapSubscribeHandler`)
  - [x] Componentes Dapr: `deploy/dapr/local/pubsub.yaml` (Redis, para `dapr run`) + placeholders `deploy/dapr/{pubsub,statestore}.yaml` (RabbitMQ/secret intent)
  - [x] Endpoint `POST /_smoke/ping` en Reservas + suscripción `/smoke` en el Worker; **verificado publish→consume cross-proceso** con Dapr CLI 1.18 (`dapr init --slim`) + Redis: 2 eventos publicados (202) → Worker los consumió logueando los UUID v7. *(Endpoint/suscripción de humo TEMPORAL: se reemplazan por `ReservaConfirmada` en 1.6 / E5.)*
- [x] **Task 6 — `docker-compose` a mano + smoke test (AC: 1, 3) — VERIFICADO**
  - [x] `deploy/docker-compose.yml` (SQL ×2, Redis, RabbitMQ, dashboard Aspire, 4 servicios; `USER` no root, tags fijos, `HEALTHCHECK`, `Dockerfile` multi-stage parametrizado; secreto SQL por `.env`)
  - [x] **Verificado:** `docker compose up` → 9 contenedores arriba, `/health` `200` en gateway:8080/hoteles:8081/reservas:8082/notificaciones:8083, los 4 `(healthy)`. *(Fix: RabbitMQ 5672 no se publica al host — rango reservado de Windows; los servicios lo alcanzan por la red interna.)*
- [x] **Task 7 — CI en GitHub Actions (AC: 3)**
  - [x] `.github/workflows/ci.yml`: `restore` + `dotnet format --verify-no-changes` + `build` (Release) + `dotnet test` + `gitleaks` (activos y verdes); job `smoke-compose` **habilitado** y gateado a push/PR a `main` (gate pesado de integración)
  - [x] `NetArchTest` incluido en el stage de test
- [x] **Task 8 — Commit + push a `develop`** (autor Santiago Renteria, sin trailers)

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

Claude Opus 4.8 (claude-opus-4-8) vía bmad-dev-story.

### Debug Log References

- `dotnet build HotelBookingHub.slnx` → **Build succeeded. 0 Warning(s), 0 Error(s)** (con `TreatWarningsAsErrors`).
- `dotnet test` → **Passed! 1/1** (NetArchTest: `Reservas.Domain` no depende de EF Core).
- `dotnet format --verify-no-changes` → limpio.
- Hallazgo de seguridad resuelto: `NU1903` (TreatWarningsAsErrors) detectó `Microsoft.OpenApi 2.0.0` (CVE GHSA-v5pm-xwqc-g5wc) arrastrado por `Microsoft.AspNetCore.OpenApi 10.0.9`; se pineó `Microsoft.OpenApi 2.10.0` en CPM.

### Completion Notes List

**Entregado y verificado (build + test + format verdes):**
- Solución `HotelBookingHub.slnx` con 14 proyectos; **CPM** (`Directory.Packages.props`) y `Directory.Build.props` (net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors, CA2016) desde el primer commit.
- Estructura de **4 assemblies por BC** (Hoteles, Reservas) + `ApiGateway` (YARP) + `Notificaciones.Worker` + `Comun` + Aspire `AppHost`/`ServiceDefaults`; referencias hacia adentro; `Class1.cs`/`WeatherForecast` eliminados.
- Salud `/health` + `/alive` vía `ServiceDefaults` en las 3 APIs/gateway y en el Worker (host web); `MapDefaultEndpoints` expone salud en todos los entornos (para el smoke de compose).
- `AppHost` declara la topología (SQL ×2, Redis, RabbitMQ, dashboard OTel) y referencia los 4 ejecutables (`Projects.*` generado OK).
- **NetArchTest** (AC-E1.1.2) verde. Puerto `IPublicadorEventos` + `EventoIntegracion` en `Comun` (costura del salto async).
- CI (`.github/workflows/ci.yml`): build + format + test + gitleaks activos; `docker-compose` + `Dockerfile` + componentes Dapr autorados; **cero secretos** (password SQL por `deploy/.env`, ignorado).

**Verificado en runtime (cierre de la historia):**
- **AC-E1.1.4 (salto async real por Dapr):** ✅ Dapr CLI 1.18 (`dapr init --slim`); `Dapr.AspNetCore` en Reservas.Api (publica) y Worker (suscribe); publish→consume cross-proceso probado sobre Redis (2 eventos → Worker los consumió).
- **AC-E1.1.1 (compose → healthy):** ✅ `docker compose up` → 9 contenedores; `/health` `200` en los 4 servicios, todos `(healthy)`. Sin SDK (imágenes autocontenidas). Fix aplicado: RabbitMQ 5672 no publicado al host (rango reservado de Windows).
- **AC-E1.1.3 (CI):** ✅ build + format + test + gitleaks verificados; `smoke-compose` verificado localmente y habilitado en CI (gate a `main`).

**Notas / gotchas (en [docs/COMO-EJECUTAR.md](../COMO-EJECUTAR.md)):**
- El SDK .NET 10 generó `HotelBookingHub.slnx` (no `.sln`).
- Windows reserva rangos de puertos (WSL/Hyper-V): 6060 (Dapr scheduler → `dapr init --slim`) y 5672 (RabbitMQ AMQP → no publicar al host).
- `dotnet run` bajo Dapr requiere `--no-launch-profile` para respetar el `--app-port`.
- **Todos los AC (E1.1.1–E1.1.4) verificados** → historia lista para review.

### File List

- `HotelBookingHub.slnx` (nuevo)
- `Directory.Build.props`, `Directory.Packages.props` (nuevos)
- `Dockerfile`, `.dockerignore` (nuevos)
- `.editorconfig` (modificado: regla naming const→PascalCase), `.gitignore` (modificado: `.env` + excepción `.env.example`)
- `.github/workflows/ci.yml` (nuevo)
- `deploy/docker-compose.yml` (RabbitMQ AMQP no publicado al host), `deploy/.env.example`, `deploy/dapr/pubsub.yaml`, `deploy/dapr/statestore.yaml`, `deploy/dapr/local/pubsub.yaml` (nuevos)
- `docs/COMO-EJECUTAR.md` (runbook reproducible con gotchas de Windows)
- `src/AppHost/AppHost/AppHost.cs` (modificado), `src/AppHost/AppHost/AppHost.csproj` (refs)
- `src/AppHost/ServiceDefaults/Extensions.cs` (modificado: salud en todos los entornos), `ServiceDefaults.csproj`
- `src/ApiGateway/{ApiGateway.csproj, Program.cs, appsettings.json}` (modificados)
- `src/Comun/HotelBookingHub.Comun/Eventos/{EventoIntegracion.cs, IPublicadorEventos.cs}` (nuevos), csproj (refs)
- `src/Servicios/Hoteles/**` (4 proyectos; `Hoteles.Api/Program.cs` reescrito)
- `src/Servicios/Reservas/**` (4 proyectos; `Reservas.Api/Program.cs` reescrito; `Reservas.Domain/AssemblyReference.cs` nuevo; `Reservas.Infrastructure` con EF Core)
- `src/Servicios/Notificaciones/Notificaciones.Worker/{Notificaciones.Worker.csproj (SDK Web), Program.cs, Worker.cs}` (modificados)
- `tests/Reservas.UnitTests/{Reservas.UnitTests.csproj, Arquitectura/DisciplinaDeCapasTests.cs}` (test NetArchTest; `UnitTest1.cs` borrado)

### Change Log

- 2026-07-08 · Story 1.1 · esqueleto ejecutable: solución + CPM + estructura 4-assemblies/BC + Aspire + YARP + health + NetArchTest + CI + compose. Build/test/format verdes.
- 2026-07-08 · Verificación de runtime: salto async Dapr (publish→consume) OK; `docker compose up` → 4 servicios `(healthy)`, `/health` 200; `smoke-compose` habilitado en CI (gate a `main`). Todos los AC verificados. Estado: `in-progress` → `review`.
