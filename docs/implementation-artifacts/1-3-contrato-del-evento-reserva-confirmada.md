# Story 1.3: Contrato del evento `ReservaConfirmada` (claves de dedup y orden congeladas)

Status: ready-for-dev

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

- [ ] **Task 1 — Definir el envelope de evento en `Comun` (AC: 1)**
  - [ ] Tipo `EventoIntegracion` (o envelope) en `src/Comun/HotelBookingHub.Comun/Eventos/` con `{ id, type, version, occurredAt, traceId, data }`
  - [ ] `type` en formato PascalCase español + semver (`ReservaConfirmada.v1`)
  - [ ] `id` = `MessageId` (dedup key); `data` incluye `aggregateId` y `version` (order key)
  - [ ] Serialización System.Text.Json `camelCase`; enums como string; `DateTimeOffset` ISO 8601; `decimal` para dinero
- [ ] **Task 2 — Payload `ReservaConfirmada.v1` (AC: 1)**
  - [ ] DTO del `data` de `ReservaConfirmada` con los campos que consumirán Notificaciones (huésped, agente, hotel, estancia, precio) y `aggregateId`/`version`
  - [ ] Documentar en el propio archivo: dedup key → la consume E5; order key → la consume E3
- [ ] **Task 3 — Contract test de snapshot (AC: 1)**
  - [ ] Proyecto `tests/Contracts/` (sin containers, rápido, corre en cada PR)
  - [ ] Test que serializa un `ReservaConfirmada.v1` de ejemplo y lo compara contra un snapshot/JSON Schema versionado
  - [ ] Verificar presencia y no-nulidad de `id`, `type` (con semver), `aggregateId`, `version`
  - [ ] Un cambio incompatible en esas claves **rompe** el test
- [ ] **Task 4 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

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

### Debug Log References

### Completion Notes List

### File List
