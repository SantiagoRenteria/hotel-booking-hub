---
baseline_commit: c1f278c9c68fcaeb00cff905de5890c9ffc4bab9
---

# Story 1.3: Contrato del evento `ReservaConfirmada` (claves de dedup y orden congeladas)

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

Como **consumidor de eventos (Worker / proyección)**,
quiero **un contrato de evento versionado con clave de deduplicación y clave de orden estables**,
para **poder deduplicar y ordenar sin acoplarme a la implementación del productor**.

> **Alcance:** solo la **forma/contrato** del evento y su test de snapshot. NO el relay, NO el consumidor real (eso es 1.6b/E5/E3). Depende de la Story 1.1 (esqueleto, `Comun`). Congelar aquí evita reabrir el productor cuando E3/E5 lleguen.

## Acceptance Criteria

1. **AC-E1.3.1 — Forma del payload congelada (contract test, solo forma).** Dado el esquema publicado de `ReservaConfirmada.v1` (envelope `{ id, type, version, occurredAt, traceId, data }`), cuando valido un evento serializado contra el snapshot del contrato, entonces el payload **contiene** y no-nulos: `id`/`MessageId` (dedup key), `aggregateId` y `version` (order key), y `type` con semver; y un cambio incompatible en esas claves **rompe** el test (snapshot/JSON Schema).

> **Alcance del contract test (party-mode: Amelia):** valida **forma/presencia**, NO comportamiento. La *semántica* de deduplicación se prueba en E5 (`AC-E5.1b.1`) y la de ordenamiento en E3 (`AC-E3.1.1`). No mezclar forma con ordering aquí.

## Tasks / Subtasks

- [x] **Task 1 — Definir el envelope de evento en `Comun` (AC: 1)**
  - [x] Tipo `EventoIntegracion` (o envelope) en `src/Comun/HotelBookingHub.Comun/Eventos/` con `{ id, type, version, occurredAt, traceId, data }`
  - [x] `type` en formato PascalCase español + semver (`ReservaConfirmada.v1`)
  - [x] `id` = `MessageId` (dedup key); `data` incluye `aggregateId` y `version` (order key)
  - [x] Serialización System.Text.Json `camelCase`; enums como string; `DateTimeOffset` ISO 8601; `decimal` para dinero
- [x] **Task 2 — Payload `ReservaConfirmada.v1` (AC: 1)**
  - [x] DTO del `data` de `ReservaConfirmada` con los campos que consumirán Notificaciones (huésped, agente, hotel, estancia, precio) y `aggregateId`/`version`
  - [x] Documentar en el propio archivo: dedup key → la consume E5; order key → la consume E3
- [x] **Task 3 — Contract test de snapshot (AC: 1)**
  - [x] Proyecto `tests/Contracts/` (sin containers, rápido, corre en cada PR)
  - [x] Test que serializa un `ReservaConfirmada.v1` de ejemplo y lo compara contra un snapshot/JSON Schema versionado
  - [x] Verificar presencia y no-nulidad de `id`, `type` (con semver), `aggregateId`, `version`
  - [x] Un cambio incompatible en esas claves **rompe** el test
- [x] **Task 4 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Contrato de eventos (fuente `architecture.md#API & Communication Patterns` / `patterns.md#Communication`)

- **Envelope** `{ id, type, version, occurredAt, traceId, data }`; **semver en `type`** (`ReservaConfirmada.v1`); compatibilidad hacia atrás.
- **`MessageId` = `id` del envelope.** Se asigna una sola vez (en el `TransactionBehavior`, antes del retry 1205) — pero **eso es 1.6b**; aquí solo se define que el campo existe y es la dedup key.
- **Order key = `{ aggregateId, version }`** — los consumidores descartan eventos con `version`/secuencia anterior.
- **`traceId` del envelope = `Activity.Current.TraceId` (W3C Trace Context)**, distinto del `traceparent` que Dapr inyecta en el CloudEvent (correlación de negocio vs tracing técnico). No confundirlos.
- Consumidor real: `Notificaciones.Worker` — el contrato NO es teórico, se congela para él y para la proyección.

### Convención de formato (fuente `patterns.md#Format`)

- JSON `camelCase` (System.Text.Json); enums como **string**; `DateTimeOffset` ISO 8601; `DateOnly` (`yyyy-MM-dd`) para estancia; **`decimal`** para dinero.

### Naming tri-idioma

- `type` en español sin tilde + semver (`ReservaConfirmada.v1`). Tipos/DTO con sufijo de patrón en inglés (`...Dto`, `...Evento`). Mensajes de negocio con tilde.

### Límites de alcance

- NO implementar el relay ni el `OutboxMessages` (1.6b), ni el consumidor (E5/E3). Solo el **contrato** + su **test de forma**.

### Anti-patrones a evitar

- Mezclar el test de forma con aserciones de ordenamiento/idempotencia (esas van en E3/E5).
- `traceId` como `Guid` propio en vez del `TraceId` de `Activity`.
- Exponer campos internos (p. ej. `Seq` bigint) en el payload.

### Testing

- `tests/Contracts/` — rápido, sin containers, en cada PR. Snapshot testing (p. ej. Verify) o JSON Schema.

### Git

- Commit + push a `develop` por cambio cerrado; autor **Santiago Renteria**, sin coautoría IA.

### Project Structure Notes

- Envelope y payloads en `src/Comun/HotelBookingHub.Comun/Eventos/`. Contract tests en `tests/Contracts/`.

### References

- [epics.md — Story 1.3](../planning-artifacts/epics.md) (AC-E1.3.1).
- [architecture.md — API & Communication Patterns / Idempotencia del consumidor](../planning-artifacts/architecture.md).
- [patterns.md](../specs/spec-hotel-booking-hub/patterns.md) (envelope, formato, communication).
- [glossary.md](../specs/spec-hotel-booking-hub/glossary.md) (eventos de dominio).

## Dev Agent Record

### Agent Model Used

Claude Opus 4.8 (claude-opus-4-8) vía bmad-dev-story.

### Debug Log References

- `dotnet test tests/Contracts` → **2/2 Passed** (`Envelope_conserva_sus_claves_y_el_type_lleva_semver`, `Data_conserva_las_claves_congeladas_y_los_formatos`).
- `dotnet build` 0 errores/0 warnings (TreatWarningsAsErrors); `dotnet format --verify-no-changes` limpio.
- Fix de CPM: el template de `Contracts.csproj` traía `Version=` inline → removido (versiones en `Directory.Packages.props`). Fix de naming: campo `_opciones` (regla `_camelCase` del `.editorconfig`).

### Completion Notes List

- **AC-E1.3.1 ✅** — contract test que congela la **forma** de `ReservaConfirmada.v1`:
  - Envelope `{ id, type, version, occurredAt, traceId, data }`; `type` con semver (`ReservaConfirmada.v1`); `id` (dedup key) presente y no vacío; `version` (order key, parte 2) presente.
  - `data` con claves congeladas (`aggregateId` = order key parte 1, + hotel/habitación/estancia/huésped/agente/precio); `DateOnly` como `yyyy-MM-dd`; dinero como `decimal` (número, no string).
  - Un rename/quita de cualquier clave **rompe** el test. Documentado: dedup key → E5; order key → E3.
- **Alcance:** solo forma. La semántica (dedupe/orden) se prueba en E5/E3. No hay relay ni consumidor aquí.
- `EventoIntegracion` (envelope) ya existía desde 1.1; esta historia añadió el payload tipado + el contract test en un proyecto `Contracts` nuevo (rápido, sin containers, corre en cada PR vía `dotnet test`).

### File List

- `src/Comun/HotelBookingHub.Comun/Eventos/ReservaConfirmadaV1.cs` (nuevo — payload tipado)
- `tests/Contracts/Contracts.csproj` (nuevo; versiones vía CPM), `tests/Contracts/ContratoReservaConfirmadaTests.cs` (nuevo)
- `HotelBookingHub.slnx` (añadido el proyecto `Contracts`)

### Change Log

- 2026-07-08 · Story 1.3 · contrato de `ReservaConfirmada.v1`: payload tipado en `Comun` + contract test de forma en `tests/Contracts` (2/2). Congela dedup key (`id`) y order key (`aggregateId`+`version`). Estado: `in-progress` → `review`.
