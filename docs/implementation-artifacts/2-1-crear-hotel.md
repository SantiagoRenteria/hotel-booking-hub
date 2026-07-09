---
baseline_commit: 7db4ecd
---

# Story 2.1: Crear hotel

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

Como **agente de viajes**,
quiero **registrar un hotel con sus datos y estado**,
para **incorporarlo a mi catálogo y maximizar comisiones**.

> **Primer slice del BC `Hoteles`.** Reutiliza tal cual el pipeline transversal de la Épica 1 (mediator propio + `Result<T>` + `ValidationBehavior` + `TypedResults`/`ToCreatedResult` en `Comun`). Aquí se llenan las capas ya scaffoldeadas de `Hoteles.*` (Domain/Application/Infrastructure/Api) con el aggregate `Hotel`, su `HotelesDbContext` (BD por servicio) y el endpoint `POST /api/v1/hoteles`. NO incluye edición/soft-delete (2.2), habilitar/deshabilitar (2.3), habitaciones (2.4) ni eventos de catálogo (2.5).

## Acceptance Criteria

1. **AC-E2.1.1 — Alta válida.** Dado un `CrearHotelCommand` con nombre, ciudad, dirección, descripción y estado, cuando se procesa, entonces responde `201 Created` con el `Hotel` creado (identidad **UUID v7**) en el estado indicado, sin envoltura `{data}` y sin exponer identificadores internos (`Seq`).
2. **AC-E2.1.2 — Validación de campos (AC negativo).** Dado un `CrearHotelCommand` con nombre o ciudad vacíos (o formato inválido), cuando se valida (`ValidationBehavior` + FluentValidation), entonces responde `400` con Problem Details (RFC 7807) enumerando los campos inválidos; no se crea hotel.

## Tasks / Subtasks

- [x] **Task 1 — Dominio `Hotel` (AC: 1)**
  - [x] Aggregate `Hotel` (UUID v7, setters privados, factory `Crear` con invariantes nombre/ciudad no vacíos)
  - [x] `EstadoHotel` enum (`Habilitado = 1`, `Deshabilitado = 2`)
  - [x] `IHotelRepository` con `Task CrearAsync(Hotel, ct)` — escritura auto-contenida en 2.1 (sin eventos → sin outbox/tx-behavior; el write-path transaccional se adopta en 2.5)
- [x] **Task 2 — Slice `CrearHotel` (AC: 1)**
  - [x] `CrearHotelCommand : ICommand<Result<HotelResponseDto>>` + `HotelResponseDto` + `CrearHotelCommandHandler`
  - [x] Handler: crea el `Hotel`, persiste vía `IHotelRepository.CrearAsync`, devuelve `Result.Ok(dto)` (sin eventos; 2.5)
- [x] **Task 3 — Validación (AC: 2)**
  - [x] `CrearHotelCommandValidator` (FluentValidation): nombre y ciudad obligatorios + `Estado` en rango (`IsInEnum`)
- [x] **Task 4 — Infraestructura `Hoteles` (AC: 1)**
  - [x] `HotelesDbContext` (BD por servicio) con claves **ADR-017** (PK UUID v7 no-clustered + `Seq` shadow clustered + `rowversion`), tabla `Hoteles`, `Estado` string. **Tabla `OutboxMessages` diferida a 2.5** (YAGNI: 2.1 no emite eventos)
  - [x] Migración EF Core `InicialHoteles` (DDL verificado: PK no-clustered + `IX_Hoteles_Seq` único clustered + rowversion) + `DesignTimeHotelesDbContextFactory`
  - [x] `HotelRepository` (Add + SaveChanges auto-contenido) + `AddHotelesInfrastructure(cadenaConexion)`
- [x] **Task 5 — Endpoint + wiring (AC: 1, 2)**
  - [x] `POST /api/v1/hoteles` con `TypedResults` + union + `Result<T>.ToCreatedResult()` (ahora en `Comun.Web`)
  - [x] `Program.cs`: `AddMediatorPipeline(CrearHotelCommand assembly)` + `AddHotelesInfrastructure(GetConnectionString("hotelesdb"))`. (Sin exception-handler de negocio: 2.1 no tiene 409; llega en 2.2. AppHost/compose/YARP: wiring diferido —el servicio ya arranca standalone—.)
- [x] **Task 6 — Tests (AC: 1, 2)**
  - [x] `Hoteles.UnitTests` (nuevo proyecto): validator (nombre/ciudad/estado), handler (happy + estado deshabilitado), pipeline (Validation corta antes del handler + enumera campo), mapeo `ToCreatedResult` (201/400). 10/10.
- [x] **Task 7 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

### Review Findings (code review 2026-07-09)

- [x] [Review][Patch] Los strings del validator no tenían `MaximumLength`; un valor sobredimensionado (p. ej. nombre de 5000 chars) pasaba la validación y reventaba en el INSERT (truncamiento SQL → `DbUpdateException` → 500, ya que Hoteles.Api aún no tiene exception-handler), incumpliendo AC-E2.1.2 (debe ser 400). **Resuelto:** `MaximumLength` en `Nombre/Ciudad/Direccion/Descripcion` acorde a las columnas + test. `[CrearHotelCommandValidator.cs]`
- Dismiss: `using Reservas.Api.Http` "huérfano" (falso positivo — `ManejadorExcepcionesNegocio` sigue ahí y `Program.cs` lo usa; build 0/0 + format limpio); `HotelResponseDto` sin Direccion/Descripcion (forma intencional por Task 2); binding del enum numérico (documentado, fuera del AC).

### Decisión de diseño (party-mode, regla #4)

- **Promoción del mapeo Result→HTTP a componente transversal** (afectaba `Reservas.Api`): party-mode (Winston + Amelia) → **opción (a)**: nuevo `HotelBookingHub.Comun.Web` (`FrameworkReference Microsoft.AspNetCore.App`) con `ToCreatedResult`; ambos `*.Api` lo referencian; `Application` NO (queda web-agnóstica). Se movió (no duplicó) desde `Reservas.Api`. La generalización del `IExceptionHandler` con una `ExcepcionNegocio` base (para que sea BC-agnóstico) se **difiere a 2.2** (que introduce el 409 de concurrencia optimista) — 2.1 no tiene excepciones de negocio.
- **Write-path transaccional (extraer vs replicar):** NO aplica a 2.1 (crear-hotel no emite eventos) → escritura auto-contenida; la decisión se toma en 2.5 (outbox de catálogo). No requirió party-mode (elección local de Hoteles, no toca Reservas).

## Dev Notes

### Reutilización del transversal (fuente Épica 1 · `Comun`)

- **NO reimplementar** el mediator ni `Result<T>`: `Comun/Mensajeria` (`ISender`, `ICommand<TResponse>`, `IRequestHandler`, `IPipelineBehavior`, `Sender`, `AddMediatorPipeline`, behaviors `Logging`/`Validation`) y `Comun/Resultados/Result.cs` ya existen y se probaron en E1. `CrearHotel` es el mismo patrón que `CrearReserva` (1.6a).
- **`ToCreatedResult` (Result→HTTP)** hoy vive en `Reservas.Api/Http/ResultadoHttpExtensions.cs`. Está registrado como **deuda diferida** (`deferred-work.md`): "promover a un proyecto web transversal cuando aparezca el 2º BC con Api". **Este es ese momento.** Mover la extensión (y `ManejadorExcepcionesNegocio`) a un componente compartido (p. ej. `Comun.Web` con `FrameworkReference Microsoft.AspNetCore.App`, o un `Comun` web-aware) que ambos BC consuman. ⚠️ **Afecta código ya implementado (Reservas.Api)** → si se decide la ubicación exacta, invocar party-mode antes de moverlo (regla del lazo autónomo).

### Claves y persistencia (fuente ADR-017 / patrón de `ReservasDbContext`)

- `Hotel`: PK `Id` UUID v7 `IsClustered(false)`; shadow `Seq bigint IDENTITY` `UseIdentityColumn()` + `HasIndex("Seq").IsUnique().IsClustered()`; shadow `RowVersion` `IsRowVersion()` (habilita la concurrencia optimista que exige 2.2). `Estado` `HasConversion<string>()`.
- **BD por servicio:** `HotelesDbContext` es independiente del de Reservas (los BC solo hablan por eventos, ADR-002). Cadena de conexión `hotelesdb`.
- Outbox: crear la tabla `OutboxMessages` en el mismo patrón que Reservas (lista para 2.5); NO implementar el relay aquí salvo que se reutilice el de infraestructura común.

### Escritura atómica (fuente 1.6b)

- Coherencia con 1.6b: el repo STAGEA (`Agregar`), la tx/`SaveChanges`/commit los posee el `TransactionBehavior`/`EjecutorTransaccional`. Decisión de diseño para dev-story: ¿extraer `EjecutorTransaccional`/`TransactionBehavior`/`ContextoMensajeria`/`ColaOutbox` a un componente compartido por BC, o replicarlos en `Hoteles.Infrastructure`? Hoy viven en `Reservas.Infrastructure`. **Afecta código implementado** → party-mode si se decide extraer (regla del lazo).

### Naming tri-idioma

- `CrearHotelCommand`/`Handler`/`Validator`, `HotelResponseDto` (sufijos inglés). Dominio: `Hotel`, `EstadoHotel` (español sin tilde). Mensajes de validación en español con tilde.

### Límites de alcance

- NO edición/soft-delete (2.2), NO habilitar/deshabilitar (2.3), NO habitaciones (2.4), NO eventos de catálogo (2.5). Aquí: alta + validación + persistencia del `Hotel`.

### Anti-patrones a evitar

- Reimplementar el mediator/`Result` en vez de reutilizar `Comun`.
- Duplicar `ToCreatedResult` en `Hoteles.Api` (promoverlo a transversal).
- Validación dentro del handler (usar `ValidationBehavior`).
- Envolver la respuesta en `{data}`; exponer `Seq`.
- Acoplar `Hoteles` a `Reservas` por referencia directa (solo por eventos).

### Testing

- `Hoteles.UnitTests` (Application) espejo de `Reservas.UnitTests`: validators, handler (happy + ramas), mapeo Result→HTTP. Fakes in-memory (repo). La integración con Testcontainers.MsSql (migración/persistencia) puede diferirse o incluirse mínima según el patrón de 1.5.

### Previous story intelligence (Épica 1)

- 1.6a: patrón exacto del slice (Command/Handler/Validator + endpoint + `ToCreatedResult`). 1.6b: write-path unificado (staging + `EjecutorTransaccional` + outbox en la misma tx; overbooking→excepción→409 vía `ManejadorExcepcionesNegocio`). 1.5: claves ADR-017 + migración EF. Reusar estos patrones al pie de la letra.
- Deuda relevante que este story puede saldar: promover `ToCreatedResult` a transversal (deferred-work E-T/2 º BC).

### Git

- Commit + push a `develop` por cambio cerrado; autor **Santiago Renteria**, sin coautoría IA.

### Project Structure Notes

- `Hoteles.Domain/Hoteles/` (aggregate + `EstadoHotel`), `Hoteles.Domain/Puertos/`, `Hoteles.Application/Hoteles/CrearHotel/`, `Hoteles.Infrastructure/Persistencia/` (+ `Migraciones/`), `Hoteles.Api/` (endpoint + Program). Tests en `tests/Hoteles.UnitTests/`.

### References

- [epics.md — Story 2.1](../planning-artifacts/epics.md) (AC-E2.1.1…2).
- [architecture.md — ADR-017 (claves) / mediator ADR-018 / Result→HTTP](../planning-artifacts/architecture.md).
- Épica 1: 1.6a (slice + endpoint), 1.6b (write-path + outbox), 1.5 (claves + migración).
- [deferred-work.md](deferred-work.md) — promover `ToCreatedResult`/`ManejadorExcepcionesNegocio` a transversal.

## Dev Agent Record

### Agent Model Used

Claude Opus 4.8 (claude-opus-4-8) vía bmad-dev-story.

### Debug Log References

- `dotnet build` 0/0; `dotnet format --verify-no-changes` limpio; **Hoteles.UnitTests 10/10**; Reservas.UnitTests 52/52 (el move de `ToCreatedResult` no rompió nada).
- Migración `InicialHoteles`: `PK_Hoteles` no-clustered (Id UUID v7) + `IX_Hoteles_Seq` único clustered + `RowVersion` rowversion + `Estado` nvarchar(20).
- Party-mode (Winston/Amelia) → `HotelBookingHub.Comun.Web` para el mapeo Result→HTTP transversal (Application permanece web-agnóstica).

### Completion Notes List

- **AC-E2.1.1 ✅** — `POST /api/v1/hoteles` → `Result.Ok` con `HotelResponseDto` (Id UUID v7, estado indicado) → `ToCreatedResult` mapea a 201; `Hotel.Crear` genera `Guid.CreateVersion7()`.
- **AC-E2.1.2 ✅** — `CrearHotelCommandValidator` (nombre/ciudad obligatorios, estado en rango) + `ValidationBehavior` corta con `Result.Invalido` (→ 400, enumera el campo) antes del handler (probado en `PipelineTests`).
- **Transversal E1 reutilizado sin reimplementar:** mediator/`Result<T>`/behaviors de `Comun`; `ToCreatedResult` promovido a `Comun.Web` (deuda de E1 saldada).
- **BC Hoteles:** `HotelesDbContext` (BD por servicio, claves ADR-017) + migración + repo auto-contenido. Sin outbox aún (2.5). `rowversion` ya presente → habilita la concurrencia optimista de 2.2.
- **Diferido:** tabla `OutboxMessages` + write-path transaccional → 2.5; generalización del exception-handler + `ExcepcionNegocio` base → 2.2; wiring AppHost/compose/YARP de Hoteles.Api → cuando se integre el gateway del catálogo. Binding del `Estado` en el body usa el enum (STJ numérico por defecto); si se requiere string en la API, añadir `JsonStringEnumConverter` (menor).

### File List

- `HotelBookingHub.slnx` (+ Comun.Web, + Hoteles.UnitTests)
- `src/Comun/HotelBookingHub.Comun.Web/` — `HotelBookingHub.Comun.Web.csproj`, `ResultadoHttpExtensions.cs` (nuevo; movido desde Reservas.Api)
- `src/Servicios/Reservas/Reservas.Api/` — `Reservas.Api.csproj` (+ ref Comun.Web), `Program.cs` (using), `Http/ResultadoHttpExtensions.cs` (eliminado)
- `src/Servicios/Hoteles/Hoteles.Domain/` — `Hoteles/Hotel.cs`, `Hoteles/EstadoHotel.cs`, `Puertos/IHotelRepository.cs` (nuevos)
- `src/Servicios/Hoteles/Hoteles.Application/` — `Hoteles.Application.csproj` (+ Comun, FluentValidation), `Hoteles/CrearHotel/{CrearHotelCommand,CrearHotelCommandHandler,CrearHotelCommandValidator}.cs` (nuevos)
- `src/Servicios/Hoteles/Hoteles.Infrastructure/` — `Hoteles.Infrastructure.csproj` (+ EF), `Persistencia/{HotelesDbContext,HotelRepository,DesignTimeHotelesDbContextFactory}.cs`, `RegistroInfraestructura.cs`, `Migraciones/*_InicialHoteles*.cs` + snapshot (nuevos)
- `src/Servicios/Hoteles/Hoteles.Api/` — `Hoteles.Api.csproj` (+ refs), `Program.cs` (endpoint + wiring)
- `tests/Reservas.UnitTests/Reservas/CrearReserva/ResultadoHttpExtensionsTests.cs` (using → Comun.Web)
- `tests/Hoteles.UnitTests/` — csproj + `CrearHotel/{Fakes,CrearHotelCommandValidatorTests,CrearHotelCommandHandlerTests,PipelineTests,ResultadoHttpMapeoTests}.cs` (nuevos)

### Change Log

- 2026-07-09 · Story 2.1 · crear-hotel: primer slice del BC Hoteles reutilizando el transversal de E1; `Hotel` aggregate + `HotelesDbContext` (ADR-017) + migración `InicialHoteles` + slice `CrearHotel` (FluentValidation) + endpoint `POST /api/v1/hoteles`. Party-mode → `Comun.Web` (promoción de `ToCreatedResult`). Diferidos: outbox/write-path→2.5, exception-handler base→2.2. Unit 10/10. Estado: `ready-for-dev` → `in-progress` → `review`.
