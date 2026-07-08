# Story 1.5: Anti-overbooking — slots `NochesHabitacion` + índice único + arbitraje

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

Como **operador del sistema**,
quiero **que dos reservas solapadas de la misma habitación sean imposibles a nivel de motor**,
para **garantizar cero overbooking aun bajo concurrencia**.

> **Núcleo del diseño.** El invariante vive en el **motor de datos** (ADR-016), no en la aplicación. Depende de la Story 1.1 (esqueleto/EF) y consume los snippets del spike 1.2 (clasificación 2627/1205). Aquí se crea el **schema** que 1.6 usará. NO implementa `CrearReservaCommand` (eso es 1.6a) — aquí se prueba el **mecanismo de persistencia/arbitraje** a nivel de repositorio.

## Acceptance Criteria

1. **AC-E1.5.0 — Esquema y migración aplicados (habilitador, precede a todo test de integración).** Dado un contenedor limpio de Testcontainers.MsSql, cuando arranca la suite de integración, entonces la migración EF Core crea las tablas `Reserva`, `NochesHabitacion` (con `UNIQUE(HabitacionId, Noche)` clustered) y `OutboxMessages` (con `UNIQUE(MessageId)`), con estrategia de migración explícita por BC.
2. **AC-E1.5.1 — El índice único arbitra el conflicto (persistencia).** Dado un slot libre `(HabitacionId, Noche)` sobre Testcontainers.MsSql, cuando dos inserciones concurrentes compiten por esa misma noche bajo `READ COMMITTED`, entonces `exactamente 1` commitea y la otra recibe violación de único (`SqlException.Number` `2627`/`2601`) → se traduce a `409` sin retry.
3. **AC-E1.5.2 — Retry acotado solo para deadlock.** Dado una inserción multi-noche que sufre un deadlock (`1205`), cuando el `TransactionBehavior` la reejecuta, entonces reintenta hasta 3 veces con backoff + jitter y los slots se insertan en orden determinístico `ORDER BY HabitacionId, Noche` para minimizar deadlocks.
4. **AC-E1.5.3 — Falso 409 (AC negativo — el riesgo silencioso).** Dado dos reservas en la **misma** habitación con estancias **adyacentes no solapadas** `[D1→D2]` y `[D2→D3]` (check-out == check-in, **no** es solape), cuando se solicitan (incluso concurrentes), entonces `ambas` confirman y `conflicts == 0`; y dado dos reservas en habitaciones **distintas** con las mismas fechas → `ambas` confirman (el `UNIQUE(HabitacionId, Noche)` no cruza habitaciones: `HabitacionId` distinto + misma `Noche` coexisten como dos filas válidas).

## Tasks / Subtasks

- [ ] **Task 1 — Modelo y schema (AC: 0)**
  - [ ] Entidad `Reserva` (aggregate root, `Reservas.Domain`) con identidad UUID v7 y `rowversion`
  - [ ] Slot `NochesHabitacion(HabitacionId, Noche, ReservaId)` con clave **clustered compuesta** `(HabitacionId, Noche)` (es el árbitro; sin surrogate)
  - [ ] `OutboxMessages` con `UNIQUE(MessageId)` (tabla lista; el relay llega en 1.6b)
  - [ ] Config EF Core: `HasKey(x=>x.Id).IsClustered(false)` + `HasIndex(x=>x.Seq).IsUnique().IsClustered()` + `Property(x=>x.Seq).UseIdentityColumn()` para `Reserva`; `NochesHabitacion.HasKey(x=>new{ x.HabitacionId, x.Noche })`
  - [ ] Migración EF Core code-first + estrategia de aplicación por BC (documentada)
- [ ] **Task 2 — Repositorio / inserción de slots con arbitraje (AC: 1, 2)**
  - [ ] Insertar los slots `[entrada, salida)` en orden determinístico `ORDER BY HabitacionId, Noche`
  - [ ] Clasificar `SqlException.Number`: `2627`/`2601` → traducir a resultado de conflicto (409, sin retry); `1205` → reintentable
  - [ ] Política de retry 1205 (3×, backoff+jitter) — puede vivir en el `TransactionBehavior` (usar snippet del spike 1.2)
- [ ] **Task 3 — Tests de integración (AC: 1, 2, 3)**
  - [ ] `Reservas.IntegrationTests` con Testcontainers.MsSql; migración aplicada al arrancar el contenedor (AC-E1.5.0)
  - [ ] Concurrencia sobre la misma noche → 1 gana, resto 2627/2601 → 409
  - [ ] **Falso 409:** adyacentes `[D1→D2]`/`[D2→D3]` → ambas OK, `conflicts == 0`; habitaciones distintas mismas fechas → ambas OK
  - [ ] Deadlock 1205 → retry acotado (simular/forzar si es viable)
- [ ] **Task 4 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Arbitraje de concurrencia (fuente ADR-016 / `concurrency-and-messaging.md` / spike 1.2)

- INSERT bajo **READ COMMITTED**; el `UNIQUE(HabitacionId, Noche)` es el árbitro.
- `2627`/`2601` → **409 sin retry**; `1205` → **retry** 3× backoff+jitter. Clasificar por `SqlException.Number`, **nunca** por mensaje.
- Slots en orden determinístico `ORDER BY HabitacionId, Noche`; reserva multi-slot **todo-o-nada** (atomicidad por transacción — la atomicidad con outbox se prueba en 1.6b).
- **Supuesto:** `Habitacion` es unidad física individual (no categoría con cupo N).

### Estrategia de claves (fuente ADR-017 / `architecture.md#Data Architecture`)

- Identidad de dominio = **UUID v7** (`Guid.CreateVersion7()`), PK **NO-clustered**, expuesta en API/eventos.
- Clustering key = `Seq bigint IDENTITY` interna, **shadow property**: nunca cruza el BC ni aparece en DTOs/eventos/logs. FK → Guid.
- `NochesHabitacion` **no** usa surrogate — su clave clustered natural es el compuesto `(HabitacionId, Noche)`. Se fragmenta por actividad de negocio → gestionar con **fill factor** + mantenimiento de índice.

### Slot / DDL (fuente `architecture-diagrams.md#Slot de inventario`)

- Una fila por `(HabitacionId, Noche)`; la unicidad impide doble reserva de la misma noche. Rango `[entrada, salida)` (la noche de salida no se reserva → por eso los adyacentes no chocan).

### Límites de alcance

- NO `CrearReservaCommand` completo (1.6a), NO validación de huésped, NO outbox-relay (1.6b), NO money test end-to-end (1.6c). Aquí: **schema + inserción de slots + arbitraje**, probado a nivel de persistencia/repositorio.

### Anti-patrones a evitar

- Garantizar el invariante en la aplicación (check-then-act) en vez del motor.
- Parsear `SqlException.Message`.
- Exponer `Seq` en DTOs/eventos.
- Probar concurrencia con EF InMemory (NO sirve — usar Testcontainers.MsSql).
- Redactar el falso 409 flojo: el borde `check-out == check-in` **no** es solape y debe confirmar ambas.

### Testing (fuente `architecture.md#Estrategia de tests`)

- `Reservas.IntegrationTests` con **Testcontainers.MsSql** (imagen pineada vía `TestKit`). El test de concurrencia vivirá aislado; en 1.6c se endurece como collection `G1` con `DisableParallelization=true`.
- Reset de estado por test (`Respawn` o tx+rollback).

### Git

- Commit + push a `develop` por cambio cerrado; autor **Santiago Renteria**, sin coautoría IA.

### Project Structure Notes

- `Reservas.Domain/` (aggregate `Reserva`, VO); `Reservas.Infrastructure/Persistencia/` (DbContext, config EF, `NochesHabitacion`), `Reservas.Infrastructure/Migraciones/`, `Reservas.Infrastructure/Outbox/` (tabla). Tests en `tests/Reservas.IntegrationTests/` + `tests/TestKit/`.

### References

- [epics.md — Story 1.5](../planning-artifacts/epics.md) (AC-E1.5.0…3).
- [architecture.md — Data Architecture (ADR-016/017)](../planning-artifacts/architecture.md).
- [concurrency-and-messaging.md](../specs/spec-hotel-booking-hub/concurrency-and-messaging.md).
- [architecture-diagrams.md — Slot de inventario](../specs/spec-hotel-booking-hub/architecture-diagrams.md).
- [decisions-adr.md](../specs/spec-hotel-booking-hub/decisions-adr.md) (ADR-016, ADR-017).
- Spike 1.2: `docs/implementation-artifacts/spikes-referencia.md` (clasificación 2627/1205, retry).

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
