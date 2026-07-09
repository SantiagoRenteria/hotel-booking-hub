---
baseline_commit: bfa6e49
---

# Story 1.6b: Atomicidad transaccional reserva + outbox

Status: review

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

- [x] **Task 1 — `TransactionBehavior` + escritura atómica (AC: 2)**
  - [x] `TransactionBehavior` (solo `ICommand`): asigna `MessageId` **una vez antes** del retry 1205 y delega en el `EjecutorTransaccional` (transacción única + retry 1205 + traducción 2627→409)
  - [x] El handler de `CrearReserva` STAGEA `Reserva` + slots (`repo.Agregar`) + fila de outbox (`IColaOutbox.Encolar`); el `EjecutorTransaccional` los confirma en el **mismo** `SaveChangesAsync()` (ADR-018)
  - [x] `OutboxMessages`: `MessageId` (= `id` del envelope de 1.3), `Type`, `Payload` (envelope serializado), `Estado` (`Pendiente`/`Enviada`), `Intentos`, `OccurredAt`, `AggregateId`, `ReclamadoEn` (lease); `UNIQUE(MessageId)` + índice de polling `(Estado, Seq)`
- [x] **Task 2 — Harness de fault-injection (AC: 1)**
  - [x] `InterceptorFallaOutbox` (`DbCommandInterceptor`): lanza al ejecutar el comando que toca `OutboxMessages`, dentro de la tx de la reserva
  - [x] Expuesto solo en tests (proyecto `Reservas.IntegrationTests`), inyectado por `SqlServerFixture.CrearContexto(interceptores)`
- [x] **Task 3 — Relay `BackgroundService` del productor (AC: 4)**
  - [x] `RelayOutbox` (`BackgroundService`) + `ProcesadorOutbox` con polling + **lease-expiry/re-claim por antigüedad** (`ReclamadoEn` nulo o vencido → no huérfanos)
  - [x] Publica al `IPublicadorEventos`; marca `Enviada` **después** de publicar; incrementa `Intentos`
  - [x] Reintentos: el test demuestra `Intentos >= 2` tras un fallo (deliveries `>= 1`, nunca asume `== 1`)
- [x] **Task 4 — Tests de integración (AC: 2, 3, 4)**
  - [x] Éxito: `Reserva == 1` y `OutboxMessages == 1` (estado `Pendiente`)
  - [x] Fallo inyectado → `Reserva == 0` y `OutboxMessages == 0` (collection `OutboxFaultInjection`, `DisableParallelization=true`)
  - [x] Relay: fila `Pendiente` → `Enviada` con `Intentos >= 1`, sin mark-sent prematuro (fallo → sigue `Pendiente`)
  - [x] Deuda etiquetada: `[Trait("VerificationDebt","E5:ConsumerIdempotency")]` y `[Trait("VerificationDebt","E3:ProjectionOrder")]`
- [x] **Task 5 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

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

Claude Opus 4.8 (claude-opus-4-8) vía bmad-dev-story.

### Debug Log References

- `dotnet build` 0/0; `Reservas.UnitTests` **48/48**; `Reservas.IntegrationTests` **8/8** (SQL Server real, estable en runs repetidos); `dotnet format --verify-no-changes` limpio.
- Migración `OutboxLeaseYPolling`: `+ReclamadoEn` (datetimeoffset nullable) + índice `IX_OutboxMessages_Estado_Seq`.
- **Decisión de diseño (aprobada por Santiago): write-path unificado.** El `TransactionBehavior`/`EjecutorTransaccional` es el único dueño de la transacción; el repo y el handler solo STAGEAN. Overbooking (2627) → `HabitacionNoDisponibleException` → `ManejadorExcepcionesNegocio` (Api) → 409. Los 4 tests de 1.5 se reescribieron para pasar por el `EjecutorTransaccional` (mismo invariante, un solo camino).
- Fix 1: `EjecutorTransaccional` limpia el `ChangeTracker` antes de cada intento y re-ejecuta la acción → el retry 1205 no re-inserta el estado del intento fallido (cierra el hallazgo del review de 1.6a sobre reusar el `DbContext`).
- Fix 2: flakiness de integración por BD compartida — el relay procesa filas globales del outbox y limpia; se aislaron TODOS los tests de outbox en la colección `OutboxFaultInjection` (contenedor propio, `DisableParallelization`), separada del contenedor de 1.5, + limpieza al inicio de cada test → orden-independiente y estable.
- Fix 3: migración EF (CRLF/BOM/namespace en bloque) y campos `static readonly` sin prefijo `_` → `dotnet format` + renombrado.

### Completion Notes List

- **AC-E1.6b.1 ✅** — `InterceptorFallaOutbox` (`DbCommandInterceptor`, solo tests) lanza determinísticamente al escribir el outbox dentro de la tx de la reserva.
- **AC-E1.6b.2 ✅** — éxito: `count(Reserva Id=X)==1` **Y** `count(OutboxMessages AggregateId=X)==1` con `Estado=Pendiente`, `Intentos=0` y `MessageId` = el del envelope.
- **AC-E1.6b.3 ✅** — fallo inyectado entre reserva y outbox → `count(Reserva)==0` **Y** `count(OutboxMessages)==0` (colección aislada, sin paralelización). Atomicidad verificada sobre SQL real.
- **AC-E1.6b.4 ✅** — el relay pasa `Pendiente→Enviada` con `Intentos>=1`, marcando `Enviada` SOLO tras publicar; con broker caído la fila sigue `Pendiente` (`Intentos>=1`) y al vencer el lease se reenvía (`Intentos>=2`). Asserts en términos persistidos (`Estado`/`Intentos`/conteos). `[DEUDA-VERIF:E5]` colapso a un efecto (idempotencia del consumidor) y `[DEUDA-VERIF:E3]` orden/convergencia de la proyección quedan etiquetados con `Trait("VerificationDebt", ...)`.
- **MessageId una-vez (ADR-018):** el `TransactionBehavior` lo asigna antes del retry vía `ContextoMensajeria` (scoped); la `ColaOutbox` lo estampa en la fila → estable ante 1205, el `UNIQUE(MessageId)` dedupea.
- **`TransactionBehavior` = eslabón interno del pipeline** (Logging → Validation → Transaction → Handler); solo cierra para `ICommand`, las queries no lo atraviesan.

### File List

- `Directory.Packages.props` (+ Microsoft.Extensions.Hosting.Abstractions)
- `src/Comun/HotelBookingHub.Comun/Mensajeria/ICommand.cs` (nuevo)
- `src/Servicios/Reservas/Reservas.Domain/Puertos/IReservaRepository.cs` (`ConfirmarAsync` → `Agregar`)
- `src/Servicios/Reservas/Reservas.Application/Abstracciones/IColaOutbox.cs` (nuevo); `Reservas/CrearReserva/CrearReservaCommand.cs` (→ `ICommand`), `CrearReservaCommandHandler.cs` (stage + encolar outbox, sin publish/catch)
- `src/Servicios/Reservas/Reservas.Infrastructure/Persistencia/` — `OutboxMessage.cs` (lease + factory + transiciones), `EjecutorTransaccional.cs` (nuevo), `ReservaRepository.cs` (→ `Agregar`), `ReservasDbContext.cs` (índice de polling); `Migraciones/*_OutboxLeaseYPolling*.cs` + snapshot
- `src/Servicios/Reservas/Reservas.Infrastructure/Mensajeria/` — `TransactionBehavior.cs`, `ContextoMensajeria.cs`, `ColaOutbox.cs` (nuevos)
- `src/Servicios/Reservas/Reservas.Infrastructure/Outbox/` — `OpcionesRelayOutbox.cs`, `ProcesadorOutbox.cs`, `RelayOutbox.cs` (nuevos)
- `src/Servicios/Reservas/Reservas.Infrastructure/RegistroInfraestructura.cs` (registra ejecutor/contexto/cola/TransactionBehavior/relay)
- `src/Servicios/Reservas/Reservas.Api/` — `Http/ManejadorExcepcionesNegocio.cs` (nuevo), `Program.cs` (AddProblemDetails + exception handler)
- `tests/Reservas.UnitTests/Reservas/CrearReserva/` — `Fakes.cs` (`ColaOutboxFake`, `RepositorioReservasFake.Agregar`), `CrearReservaCommandHandlerTests.cs`, `PipelineTests.cs` (reworked)
- `tests/Reservas.IntegrationTests/` — `SqlServerFixture.cs` (overload con interceptores), `AntiOverbookingTests.cs` (vía `EjecutorTransaccional`), `InterceptorFallaOutbox.cs`, `PublicadoresFake.cs`, `HelpersOutbox.cs`, `OutboxAtomicidadYRelayTests.cs`, `OutboxFaultInjectionTests.cs` (nuevos)

### Change Log

- 2026-07-08 · Story 1.6b · atomicidad reserva+outbox: `TransactionBehavior` + `EjecutorTransaccional` (write-path unificado, dueño único de la tx) + `IColaOutbox`/`ColaOutbox` (staging del outbox en el mismo `SaveChanges`) + `OutboxMessage` con lease + relay `BackgroundService` (`ProcesadorOutbox`) + fault-injection (`DbCommandInterceptor`). Overbooking → excepción → 409 (Api). Migración EF `OutboxLeaseYPolling`. Unit 48/48, integración 8/8 (Testcontainers). Estado: `ready-for-dev` → `in-progress` → `review`.
