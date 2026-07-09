# Story 2.1: Crear hotel

Status: ready-for-dev

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

- [ ] **Task 1 — Dominio `Hotel` (AC: 1)**
  - [ ] Aggregate `Hotel` en `Hoteles.Domain/Hoteles/` con identidad UUID v7 (`Guid.CreateVersion7()`), setters privados y factory `Crear(nombre, ciudad, direccion, descripcion, estado)`; invariantes en el dominio (no vacíos como defensa en profundidad)
  - [ ] `EstadoHotel` enum (`Habilitado = 1`, `Deshabilitado = 2`) — el estado de publicación; el soft-delete (2.2) y el toggle (2.3) llegan después
  - [ ] `IHotelRepository` (puerto) en `Hoteles.Domain/Puertos/` con `void Agregar(Hotel hotel)` (staging; la tx la posee el `TransactionBehavior`, coherente con 1.6b)
- [ ] **Task 2 — Slice `CrearHotel` (AC: 1)**
  - [ ] `CrearHotelCommand` (`: ICommand<Result<HotelResponseDto>>`) + `HotelResponseDto` (Id UUID v7, Nombre, Ciudad, Estado) + `CrearHotelCommandHandler` en `Hoteles.Application/Hoteles/CrearHotel/`
  - [ ] Handler: crea el `Hotel`, lo stagea vía `IHotelRepository.Agregar`, devuelve `Result.Ok(dto)` (sin publicar eventos aún; los eventos de catálogo son 2.5)
- [ ] **Task 3 — Validación (AC: 2)**
  - [ ] `CrearHotelCommandValidator` (FluentValidation): nombre y ciudad obligatorios; dirección/descripción según reglas; regex con `matchTimeout` si aplica (anti-ReDoS)
- [ ] **Task 4 — Infraestructura `Hoteles` (AC: 1)**
  - [ ] `HotelesDbContext` (BD por servicio) con claves **ADR-017** (PK UUID v7 no-clustered + `Seq bigint IDENTITY` shadow clustered + `rowversion`), tabla `Hoteles`, `Estado` como string; tabla `OutboxMessages` (lista para 2.5, mismo patrón que Reservas)
  - [ ] Migración EF Core `InicialHoteles` + `DesignTimeHotelesDbContextFactory`
  - [ ] `HotelRepository` (staging) + `AddHotelesInfrastructure(cadenaConexion)` (DbContext + repo; `EjecutorTransaccional`/`TransactionBehavior`/outbox reutilizando el patrón de 1.6b — extraer lo común o replicar por BC según decisión de diseño)
- [ ] **Task 5 — Endpoint + wiring (AC: 1, 2)**
  - [ ] `POST /api/v1/hoteles` en `Hoteles.Api` con `TypedResults` + union type explícito + `Result<T>.ToCreatedResult()`
  - [ ] `Program.cs`: `AddMediatorPipeline(typeof(CrearHotelCommand).Assembly)` + `AddHotelesInfrastructure(...)` + `ManejadorExcepcionesNegocio` (o su equivalente transversal); registrar en AppHost/compose/YARP
- [ ] **Task 6 — Tests (AC: 1, 2)**
  - [ ] Unit (Application): happy path → `Result.Ok` con id UUID v7 + estado; validación → `Result.Invalido` por campo (pipeline corta antes del handler); mapeo `ToCreatedResult` (201/400)
  - [ ] `Hoteles.UnitTests` (nuevo proyecto) siguiendo el patrón de `Reservas.UnitTests`
- [ ] **Task 7 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

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

### Debug Log References

### Completion Notes List

### File List
