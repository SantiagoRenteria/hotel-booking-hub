# Story 1.6b: Atomicidad transaccional reserva + outbox

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

Como **operador del sistema**,
quiero **que la reserva y su evento de outbox se escriban en la misma transacción o ninguna**,
para **no dejar eventos huérfanos ni reservas sin notificar**.

> **Alcance:** atomicidad de la escritura (dominio + outbox en un `SaveChanges`) + resiliencia del relay del **productor**, probado con fault-injection. NO concurrencia (1.6c). Depende de 1.5 (schema `OutboxMessages`), 1.6a (`CrearReserva`) y 1.2 (patrón `TransactionBehavior`). Aquí se materializa el `TransactionBehavior` + relay real (con publisher fake de Dapr).

## Acceptance Criteria

1. **AC-E1.6b.1 — Harness de fault-injection (habilitador).** Dado un `DbCommandInterceptor` (o hook de `SaveChanges`) que puede lanzar entre el INSERT de `Reserva` y el de `OutboxMessages`, cuando se activa en un test, entonces provoca el fallo transaccional de forma determinista.
2. **AC-E1.6b.2 — Éxito: una fila de cada una.** Dado una reserva confirmada, cuando inspecciono la BD tras el commit, entonces `count(Reserva WHERE AggregateId=X) == 1` **Y** `count(OutboxMessages WHERE AggregateId=X) == 1` con estado `Pendiente`.
3. **AC-E1.6b.3 — Fallo: ninguna de las dos (atomicidad).** Dado un fallo inyectado entre el insert de `Reserva` y el de `OutboxMessages`, cuando la transacción se resuelve, entonces `count(Reserva Confirmada) == 0` **Y** `count(OutboxMessages) == 0` (las dos o ninguna). *(Collection `OutboxFaultInjection` aislada, `DisableParallelization = true`.)*
4. **AC-E1.6b.4 — At-least-once del productor (en términos persistidos).** Dado una reserva confirmada con su fila de outbox `Pendiente` y el relay corriendo, cuando el relay publica al `IPublicadorEventos` fake (con reintentos simulados), entonces la fila pasa a `Enviada` con `intentos >= 1`; sin "mark sent" prematuro (no se marca enviada antes de publicar). **Y** `[DEUDA-VERIF:E5]` el colapso a un solo **efecto** (idempotencia del consumidor, `deliveries` de runtime) se verifica en E5. **Y** `[DEUDA-VERIF:E3]` el orden/convergencia de la proyección se verifica en E3.

## Tasks / Subtasks

- [ ] **Task 1 — `TransactionBehavior` + escritura atómica (AC: 2)**
  - [ ] `TransactionBehavior` (solo comandos): abre transacción, asigna `MessageId` **una vez antes** del retry 1205, aplica el retry 1205 (snippet de 1.2)
  - [ ] El handler de `CrearReserva` (1.6a) escribe `Reserva` + slots + fila `OutboxMessages` en el **mismo** `DbContext.SaveChangesAsync()` (ADR-018)
  - [ ] `OutboxMessages`: `MessageId` (= `id` del envelope de 1.3), `type`, `payload`, `estado` (`Pendiente`/`Enviada`), `intentos`, `occurredAt`, `AggregateId`; `UNIQUE(MessageId)`
- [ ] **Task 2 — Harness de fault-injection (AC: 1)**
  - [ ] `DbCommandInterceptor` (o hook de `SaveChanges`) configurable para lanzar entre el INSERT de dominio y el de outbox
  - [ ] Expuesto solo en tests (no en producción)
- [ ] **Task 3 — Relay `BackgroundService` del productor (AC: 4)**
  - [ ] Relay con polling + **lease-expiry/re-claim por antigüedad** (no solo por estado → sin mensajes huérfanos)
  - [ ] Publica al `IPublicadorEventos` fake; marca `Enviada` **después** de publicar; incrementa `intentos`
  - [ ] Reintentos simulados que demuestran `deliveries >= 1` (nunca asume `== 1`)
- [ ] **Task 4 — Tests de integración (AC: 2, 3, 4)**
  - [ ] Éxito: `Reserva == 1` y `OutboxMessages == 1` (estado `Pendiente`)
  - [ ] Fallo inyectado → `Reserva == 0` y `OutboxMessages == 0` (collection `OutboxFaultInjection`, `DisableParallelization=true`)
  - [ ] Relay: fila `Pendiente` → `Enviada` con `intentos >= 1`, sin mark-sent prematuro
  - [ ] Etiquetar la deuda: `[Trait("VerificationDebt","E5:ConsumerIdempotency")]` y `[Trait("VerificationDebt","E3:ProjectionOrder")]`
- [ ] **Task 5 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Outbox (fuente `architecture.md#API & Communication Patterns` / ADR-018 / `concurrency-and-messaging.md`)

- **Outbox manual, at-least-once por diseño.** `OutboxMessages` en la **misma transacción** EF que el cambio de dominio.
- **`MessageId` se genera una sola vez antes del `TransactionBehavior`/retry.** Si se regenera en el retry 1205, el `UNIQUE(MessageId)` no dedupea. Anti-patrón crítico.
- **Relay `BackgroundService`** con polling + lease-expiry/re-claim por antigüedad; publica a Dapr pub/sub (aquí fake). La **no-duplicación se garantiza en el efecto** (dedupe del consumidor, E5), NO en el wire.
- Dapr outbox nativo **descartado** (acoplaría persistencia al state store de Dapr).

### Honestidad del alcance (party-mode: Murat)

- El criterio de esta historia es **atomicidad + resiliencia del PRODUCTOR**, NO exactly-once end-to-end.
- **`deliveries >= 1` NUNCA `== 1`** en E1. El colapso a un efecto único es del consumidor idempotente (E5). Redactar los asserts en términos **persistidos** (`estado`, `intentos`, conteos de filas), no en vocabulario de runtime que aún no existe.

### Límites de alcance

- NO concurrencia / money test (1.6c). NO consumidor real / idempotencia (E5). NO proyección (E3). Aquí: **atomicidad + relay del productor**.

### Anti-patrones a evitar

- Regenerar `MessageId` en el handler o en el loop de retry.
- Marcar la fila `Enviada` **antes** de publicar (mark-sent prematuro → pérdida).
- Asumir `deliveries == 1`.
- Relay que reclama solo por estado y no por antigüedad (deja huérfanos).

### Testing (fuente `architecture.md#Estrategia de tests`)

- `Reservas.IntegrationTests` con Testcontainers.MsSql. La collection `OutboxFaultInjection` va **aislada** con `DisableParallelization=true` y su propio contenedor; el "broker caído" se inyecta como **fake controlable de Dapr**, no tumbando infra real.
- Reset por test (`Respawn` o tx+rollback).

### Previous story intelligence (plan-based)

- De 1.5: schema `OutboxMessages`+`UNIQUE(MessageId)`, clasificación 2627/1205, retry. De 1.2: patrón `TransactionBehavior`. De 1.6a: `CrearReservaCommandHandler` y el `IPublicadorEventos` fake. (Actualizar con patrones reales tras implementarlas.)

### Git

- Commit + push a `develop` por cambio cerrado; autor **Santiago Renteria**, sin coautoría IA.

### Project Structure Notes

- `Comun/Behaviors/TransactionBehavior`, `Reservas.Infrastructure/Outbox/` (tabla + relay `BackgroundService`), `Reservas.Infrastructure/Persistencia/` (interceptor de fault-injection en test). Tests en `tests/Reservas.IntegrationTests/` (collection `OutboxFaultInjection`) + `tests/TestKit/`.

### References

- [epics.md — Story 1.6b](../planning-artifacts/epics.md) (AC-E1.6b.1…4).
- [architecture.md — Outbox / Idempotencia del consumidor / ADR-018](../planning-artifacts/architecture.md).
- [concurrency-and-messaging.md](../specs/spec-hotel-booking-hub/concurrency-and-messaging.md) (outbox, lease/re-claim).
- Stories 1.2 (TransactionBehavior), 1.3 (MessageId=envelope.id), 1.5 (schema outbox), 1.6a (handler).

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
