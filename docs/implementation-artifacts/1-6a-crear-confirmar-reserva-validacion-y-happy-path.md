# Story 1.6a: Crear-confirmar reserva — validación y happy path

Status: ready-for-dev

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

- [ ] **Task 1 — Slice `CrearReserva` (AC: 4)**
  - [ ] `CrearReservaCommand` + `CrearReservaCommandHandler` en `Reservas.Application/Reservas/CrearReserva/`
  - [ ] VOs de dominio: `Huesped` (nombres, apellidos, fecha nacimiento, género, `Documento` tipo+número, email, teléfono), `ContactoEmergencia` (nombre completo, teléfono)
  - [ ] Handler: valida habitación activa/disponible (proyección llega en E3; aquí usar dato sembrado/1.5), calcula precio (1.4), crea `Reserva` `Confirmada` e inserta slots (1.5)
  - [ ] Publica `ReservaConfirmada` vía `IPublicadorEventos` fake (contrato de 1.3)
- [ ] **Task 2 — Validación (AC: 2, 3)**
  - [ ] `CrearReservaCommandValidator` (FluentValidation): todos los campos de cada huésped obligatorios + formatos (email, teléfono, documento; fecha de nacimiento coherente); `ContactoEmergencia` obligatorio
  - [ ] `ValidationBehavior` corta con `Result.Invalid` → `400` ValidationProblem (`{errors:{campo:[msgs]}}`) antes de tocar la BD
- [ ] **Task 3 — Endpoint + mapeo Result→HTTP (AC: 4)**
  - [ ] `POST /api/v1/reservas` en `Reservas.Api` (Minimal API) con `TypedResults` + union type explícito (`Results<Created<ReservaResponseDto>, ValidationProblem, ...>`)
  - [ ] Extensión `Result<T>.ToHttpResult()`; éxito → `201 Created` con `ReservaResponseDto` (id UUID v7, estado, precio)
- [ ] **Task 4 — Tests (AC: 1, 2, 3, 4)**
  - [ ] Unit de Application: huésped inválido→400 (cada campo), contacto emergencia ausente→400, happy path→201 con precio correcto
  - [ ] `IPublicadorEventos` fake in-memory registrado en los tests
- [ ] **Task 5 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

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

### Debug Log References

### Completion Notes List

### File List
