---
baseline_commit: d4cf800
---

# Story 1.6a: Crear-confirmar reserva — validación y happy path

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

Como **viajero**,
quiero **reservar una habitación disponible registrando mis datos y un contacto de emergencia, y confirmarla en una sola operación**,
para **obtener alojamiento con confirmación inmediata**.

> **Alcance:** la lógica de **aplicación** (comando + validación + orquestación + happy path). La concurrencia (1.6c) y la atomicidad transaccional con outbox (1.6b) se prueban por separado, en otra capa. Depende de 1.4 (`CalculadorPrecio`) y 1.5 (write path de slots + schema). Publisher de eventos = **fake in-memory** (Dapr real NO es dependencia de estos tests).

## Acceptance Criteria

1. **AC-E1.6a.1 — Publisher de eventos fake in-memory (habilitador).** Dado los tests de E1, cuando un handler necesita publicar un evento, entonces se inyecta un `IPublicadorEventos` **fake in-memory**; los tests de E1 no dependen del sidecar de Dapr ni de un broker real.
2. **AC-E1.6a.2 — Datos de cada huésped obligatorios (AC negativo).** Dado un `CrearReservaCommand` con un huésped al que le falta un campo (nombres, apellidos, fecha de nacimiento, género, tipo/número de documento, email o teléfono) o con formato inválido, cuando se valida (`ValidationBehavior` + FluentValidation), entonces responde `400` con Problem Details (RFC 7807) enumerando los campos inválidos; no se crea reserva.
3. **AC-E1.6a.3 — Contacto de emergencia obligatorio (AC negativo).** Dado un `CrearReservaCommand` sin `ContactoEmergencia` (nombre completo + teléfono), cuando se valida, entonces responde `400` con Problem Details; no se crea reserva.
4. **AC-E1.6a.4 — Confirmación exitosa expone el precio.** Dado un comando válido sobre una habitación disponible, cuando se confirma, entonces responde `201` con la `Reserva` en estado `Confirmada`, el precio total (AC-E1.4.1) y su identificador (UUID v7).

## Tasks / Subtasks

- [x] **Task 1 — Slice `CrearReserva` (AC: 4)**
  - [x] `CrearReservaCommand` + `CrearReservaCommandHandler` en `Reservas.Application/Reservas/CrearReserva/`
  - [x] VOs de dominio: `Huesped` (nombres, apellidos, fecha nacimiento, género, `Documento` tipo+número, email, teléfono), `ContactoEmergencia` (nombre completo, teléfono)
  - [x] Handler: valida habitación activa/disponible (puerto `IDisponibilidadHabitacion`, sembrado; proyección real en E3), calcula precio (1.4), crea `Reserva` `Confirmada` e inserta slots vía `IReservaRepository` (1.5)
  - [x] Publica `ReservaConfirmada` vía `IPublicadorEventos` fake (contrato de 1.3)
- [x] **Task 2 — Validación (AC: 2, 3)**
  - [x] `CrearReservaCommandValidator` (FluentValidation): todos los campos de cada huésped obligatorios + formatos (email, teléfono, documento; fecha de nacimiento coherente); `ContactoEmergencia` obligatorio. Regex con `matchTimeout` (anti-ReDoS)
  - [x] `ValidationBehavior` corta con `Result.Invalido` → `400` ValidationProblem (`{errors:{campo:[msgs]}}`) antes de tocar la BD
- [x] **Task 3 — Endpoint + mapeo Result→HTTP (AC: 4)**
  - [x] `POST /api/v1/reservas` en `Reservas.Api` (Minimal API) con `TypedResults` + union type explícito (`Results<Created<ReservaResponseDto>, ValidationProblem, ProblemHttpResult>`)
  - [x] Extensión `Result<T>.ToCreatedResult()`; éxito → `201 Created` con `ReservaResponseDto` (id UUID v7, estado, precio)
- [x] **Task 4 — Tests (AC: 1, 2, 3, 4)**
  - [x] Unit de Application: huésped inválido→400 (cada campo), contacto emergencia ausente→400, happy path→201 con precio correcto; pipeline (Validation corta antes del handler); mapeo Result→HTTP (201/400/409/404)
  - [x] `IPublicadorEventos` fake in-memory registrado en los tests
- [x] **Task 5 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

### Review Findings (code review 2026-07-08)

- [x] [Review][Patch] La clase base `Result` no implementa `IResultadoInvalidable<Result>`; un futuro `IRequest<Result>` (command sin payload) se saltaría el `ValidationBehavior` en silencio (MS DI omite el open generic cuya restricción no se cumple). **Resuelto:** `Result` ahora implementa `IResultadoInvalidable<Result>`. `[Comun/Resultados/Result.cs]`
- [x] [Review][Patch] Sin tope de noches, una estancia de años genera cientos de miles de slots en una tx (DoS alcanzable con entrada bien formada). **Resuelto:** regla `MaxNoches = 365` en `CrearReservaCommandValidator` + test. `[CrearReservaCommandValidator.cs]`
- [x] [Review][Patch] AC-E1.6a.2/.3 piden que el 400 enumere los campos inválidos; los tests solo comprobaban el status code. **Resuelto:** `PipelineTests` asevera que `Result.Errores` contiene la clave del campo inválido. `[tests/.../PipelineTests.cs]`
- [x] [Review][Defer] `Sender` compone por reflexión (`MethodInfo.Invoke`): un handler/behavior NO-`async` que lance síncrono saldría envuelto en `TargetInvocationException`. Latente (todos los handlers actuales son `async`). Al añadir el `TransactionBehavior` (1.6b), desenvolver `InnerException` en el `Sender`. `[Comun/Mensajeria/Sender.cs]` — deferido a 1.6b.
- [x] [Review][Defer] La capacidad de la habitación no se contrasta con el nº de huéspedes (una habitación de capacidad 2 acepta 10). Regla de negocio de búsqueda/catálogo. `[CrearReservaCommandHandler.cs]` — deferido a E3.
- [x] [Review][Defer] Naming vs lista cerrada de sufijos: `Sender`/`ISender` (nombres de contrato del mediator, ADR-018) y el placeholder `PublicadorEventosLog` (sufijo `Log`). Whitelistear los términos del mediator y renombrar/eliminar el placeholder cuando se materialice el check de sufijos + `AGENTS.md`. `[Comun/Mensajeria, Reservas.Infrastructure/Mensajeria]` — deferido a E-T.

## Dev Notes

### Diseño (fuente `architecture.md` / `patterns.md`)

- Slice por caso de uso: `Reservas.Application/Reservas/CrearReserva/` (Command + Handler + Validator juntos).
- **Mediator (ADR-018):** `TResponse = Result<T>`; pipeline `Logging → Validation → Transaction → Outbox → Handler`. En 1.6a el foco es Validation + Handler + happy path; el `TransactionBehavior`/outbox se endurece en 1.6b (aquí el publisher es fake y la escritura puede ser directa, pero **respeta la firma del mediator** para no reescribir en 1.6b).
- **Errores:** `Result<T>` en flujos esperados; Problem Details RFC 7807 (`application/problem+json`) en error; nada de `{data,error}` ni 200-con-error.
- Aggregate `Reserva` con factory (`Reserva.Crear(...)`) y setters privados; invariantes en el dominio.

### Reglas de validación (fuente epics FR-10/11)

- Huésped: nombres, apellidos, fecha de nacimiento, género, tipo y número de documento, email, teléfono — **todos obligatorios**, con formato válido (email/teléfono/documento; fecha de nacimiento coherente).
- `ContactoEmergencia`: nombre completo + teléfono, obligatorio.
- Toda `Regex` con `matchTimeout` explícito (anti-ReDoS, 100 ms–2 s) — fuente `security-and-quality.md`.

### Naming tri-idioma

- `CrearReservaCommand`, `CrearReservaCommandHandler`, `CrearReservaCommandValidator`, `ReservaResponseDto` (sufijos en inglés). Dominio: `Reserva`, `Huesped`, `ContactoEmergencia`, `Documento` (español sin tilde). Mensajes de validación en español con tilde.

### Límites de alcance

- NO money test / concurrencia (1.6c). NO fault-injection / atomicidad outbox (1.6b). NO búsqueda real por proyección (E3) — usa un dato de habitación sembrado (VO/stub) para el happy path.
- Publisher = fake in-memory. NO cablear Dapr real aquí.

### Anti-patrones a evitar

- Endpoints con `IResult` desnudo y mapeo manual `Result`→HTTP por endpoint (usar `TypedResults` + union + `ToHttpResult()`).
- Envolver la respuesta en `{data,...}`.
- Poner la validación dentro del handler en vez del `ValidationBehavior`.
- Acoplar el test a Dapr/broker real.

### Testing

- `Reservas.UnitTests` (Application). Mínimos: validators (happy + cada regla), handler (happy + ramas), endpoint (201/400). El `IPublicadorEventos` fake evita dependencia de infraestructura.

### Previous story intelligence (plan-based)

- De 1.4: `CalculadorPrecio` y `Estancia` ya existen en `Reservas.Domain/Servicios`. Reúsalos — no recalcules el precio a mano.
- De 1.5: el write path de slots y la clasificación 2627/1205 ya existen; el handler debe insertar slots vía ese camino. (Actualizar esta sección con los patrones reales tras implementar 1.4/1.5.)

### Git

- Commit + push a `develop` por cambio cerrado; autor **Santiago Renteria**, sin coautoría IA.

### Project Structure Notes

- `Reservas.Application/Reservas/CrearReserva/` (slice). `Reservas.Domain/` (`Reserva`, VOs). `Reservas.Api/` (endpoint). Tests en `tests/Reservas.UnitTests/`.

### References

- [epics.md — Story 1.6a](../planning-artifacts/epics.md) (AC-E1.6a.1…4).
- [architecture.md — Contrato del mediator (ADR-018) / Implementation Patterns](../planning-artifacts/architecture.md).
- [patterns.md](../specs/spec-hotel-booking-hub/patterns.md) (Result→HTTP, Problem Details, TypedResults).
- [security-and-quality.md](../specs/spec-hotel-booking-hub/security-and-quality.md) (validación, ReDoS).
- Stories 1.4 (`CalculadorPrecio`), 1.5 (slots), 1.3 (evento `ReservaConfirmada`).

## Dev Agent Record

### Agent Model Used

Claude Opus 4.8 (claude-opus-4-8) vía bmad-dev-story.

### Debug Log References

- `dotnet test Reservas.UnitTests` — **48/48** (18 previos + 30 nuevos de 1.6a: validators, handler, pipeline, mapeo Result→HTTP). `dotnet build` 0/0; `dotnet format --verify-no-changes` limpio.
- Fix 1: FluentValidation 11 eliminó el enum `EmailValidationMode` → se usa `.EmailAddress()` (modo simple "contiene @", sin ReDoS).
- Fix 2: colisión de namespace — el namespace de test `Reservas.UnitTests.Reservas.*` hacía que `Reservas.Domain` resolviera al sub-namespace `Reservas` en el test de arquitectura → renombrado a `Reservas.UnitTests.CrearReserva`.
- Fix 3: IDE1006 — campo `private static readonly` requiere prefijo `_` (`_sinErrores`).

### Completion Notes List

- **AC-E1.6a.1 ✅** — `PublicadorEventosFake` in-memory inyectado en tests de handler y pipeline; ningún test de E1 depende de Dapr ni broker.
- **AC-E1.6a.2 ✅** — `HuespedDtoValidator` exige cada campo (nombres, apellidos, fecha nacimiento coherente, género, tipo/número documento, email, teléfono) con formato válido; `ValidationBehavior` corta con `Result.Invalido` (→ 400 `ValidationProblem`) antes del handler (probado en `PipelineTests`).
- **AC-E1.6a.3 ✅** — `ContactoEmergencia` obligatorio (nombre + teléfono) validado a nivel de comando.
- **AC-E1.6a.4 ✅** — happy path devuelve `Result.Ok` con `ReservaResponseDto` (id UUID v7, estado `Confirmada`, `PrecioTotal` = `(100.50+19.99)*2 = 240.98` reusando `CalculadorPrecio` de 1.4); `ToCreatedResult` mapea a `201`. Ramas de negocio: habitación inexistente/inactiva → 404; carrera perdida en el motor (`HabitacionNoDisponibleException` de 1.5) → 409 sin publicar evento.
- **Mediator propio (ADR-018)** materializado en `Comun`: `ISender`/`IRequestHandler`/`IPipelineBehavior` (Decorator) + `Sender` (composición por reflexión) + `AddMediatorPipeline` (scan de assembly). Behaviors `Logging` y `Validation`; `TransactionBehavior`/outbox se insertan en 1.6b (pipeline ya preparado). `Result`/`Result<T>` con `IResultadoInvalidable<TSelf>` (miembro estático abstracto) para que `ValidationBehavior` cortocircuite sin reflexión.
- **Dependencias nuevas** (dentro del alcance de la historia/ADR-018): `FluentValidation` 11.11.0, `Microsoft.Extensions.DependencyInjection.Abstractions` y `Microsoft.Extensions.Logging.Abstractions` 10.0.0 (en `Comun`, sin arrastrar ASP.NET Core a Application).
- **Deuda anotada (deferred-work):** persistir huéspedes/contacto en el agregado (mapeo EF + migración + test de integración) → 1.6b; `IDisponibilidadHabitacion` y el publisher son placeholders (proyección real en E3, outbox→Dapr en 1.6b); `ToCreatedResult` vive en `Reservas.Api` y debe promoverse a un proyecto web transversal cuando aparezca el 2º BC con Api.

### File List

- `Directory.Packages.props` (+ FluentValidation, DI.Abstractions, Logging.Abstractions)
- `src/Comun/HotelBookingHub.Comun/` — `HotelBookingHub.Comun.csproj` (mod), `Resultados/Result.cs`, `Mensajeria/{IRequest,IRequestHandler,IPipelineBehavior,ISender,Sender,RegistroMediador}.cs`, `Mensajeria/Behaviors/{LoggingBehavior,ValidationBehavior}.cs` (nuevos)
- `src/Servicios/Reservas/Reservas.Domain/Reservas/` — `Documento.cs`, `Huesped.cs`, `ContactoEmergencia.cs` (nuevos); `Puertos/IDisponibilidadHabitacion.cs` (nuevo)
- `src/Servicios/Reservas/Reservas.Application/` — `Reservas.Application.csproj` (mod), `Reservas/CrearReserva/{CrearReservaCommand,ReservaResponseDto,CrearReservaCommandHandler,CrearReservaCommandValidator,ExpresionesValidacion}.cs` (nuevos)
- `src/Servicios/Reservas/Reservas.Infrastructure/` — `RegistroInfraestructura.cs`, `Mensajeria/PublicadorEventosLog.cs`, `Disponibilidad/DisponibilidadHabitacionSembrada.cs` (nuevos)
- `src/Servicios/Reservas/Reservas.Api/` — `Reservas.Api.csproj` (mod), `Program.cs` (endpoint + wiring), `Http/ResultadoHttpExtensions.cs` (nuevo)
- `tests/Reservas.UnitTests/` — `Reservas.UnitTests.csproj` (mod), `Reservas/CrearReserva/{Fakes,DatosPrueba,CrearReservaCommandValidatorTests,CrearReservaCommandHandlerTests,PipelineTests,ResultadoHttpExtensionsTests}.cs` (nuevos)

### Change Log

- 2026-07-08 · Story 1.6a · crear-confirmar reserva (Application): mediator propio (ADR-018) + `Result<T>` en `Comun`; slice `CrearReserva` (command/handler/validator) con FluentValidation + `ValidationBehavior`; VOs `Huesped`/`ContactoEmergencia`/`Documento`; endpoint `POST /api/v1/reservas` con `TypedResults` + mapeo Result→HTTP; publisher/disponibilidad placeholder. Unit 48/48. Estado: `ready-for-dev` → `in-progress` → `review`.
