---
baseline_commit: 99dec38cfbbefc7a769b6802797d7f1c1e6c2554
---

# Story 1.2: Spikes de validación de ejecución (Sprint 0, timeboxed)

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

Como **equipo de desarrollo**,
quiero **validar en un timebox que el arbitraje por índice único y el wiring del mediator funcionan sobre infraestructura real**,
para **confirmar el diseño (o disparar el Plan B) antes de invertir en el core**.

> **Naturaleza:** SPIKE. El código es **desechable** (throwaway), NO cuenta como entregable productivo ni exige cobertura ≥80%. El valor es el **aprendizaje** (snippets de referencia) y una decisión **go/no-go**. Vive en una rama de spike; lo que sobrevive es la documentación de la Task 3. Depende de la Story 1.1 (esqueleto + Testcontainers disponible).

## Acceptance Criteria

1. **AC-E1.2.1 — Spike de arbitraje de concurrencia (go/no-go).** Dado un `NochesHabitacion` con `UNIQUE(HabitacionId, Noche)` sobre Testcontainers.MsSql, cuando dos INSERT concurrentes compiten por la misma noche bajo `READ COMMITTED`, entonces uno commitea y el otro recibe `SqlException.Number` `2627`/`2601` (clasificado por número, **sin** parsear el mensaje) y un `1205` (deadlock) se distingue como reintentable. **Criterio de aborto:** si no se logra la garantía en el timebox, se documenta y se dispara el **Plan B** (`SERIALIZABLE` sin retry — revierte parcialmente ADR-016, ya trazado).
2. **AC-E1.2.2 — Spike del pipeline del mediator (go/no-go).** Dado un `IRequestHandler<TRequest, TResponse>` con `TResponse = Result`, cuando se ejecuta un comando trivial a través del pipeline `Logging → Validation → Transaction → Outbox → Handler`, entonces los behaviors se componen en ese orden literal y el insert de dominio y el de `OutboxMessages` comparten el mismo `SaveChangesAsync` (ADR-018).
3. **AC-E1.2.3 — El aprendizaje sobrevive a la rama del spike.** Dado que el código del spike es desechable, cuando se cierra el spike, entonces el arbitraje `2627`/`1205` y el wiring del mediator quedan documentados como snippet de referencia (alimentan 1.5 y 1.6b); el conocimiento no muere con la rama throwaway.

## Tasks / Subtasks

- [x] **Task 1 — Spike de arbitraje 2627/1205 (AC: 1)**
  - [x] Tabla temporal `NochesHabitacion(HabitacionId, Noche, ReservaId)` con `UNIQUE(HabitacionId, Noche)` sobre Testcontainers.MsSql
  - [x] Test que lanza 2..N INSERT concurrentes sobre la misma `(HabitacionId, Noche)` bajo `READ COMMITTED`
  - [x] Clasificar por `SqlException.Number`: `2627`/`2601` (único) vs `1205` (deadlock); **pattern matching por Number, nunca por mensaje**
  - [x] Confirmar: exactamente 1 gana; los perdedores por único son determinísticos (409 sin retry); 1205 es reintentable
  - [x] **Go/no-go documentado.** Si no cierra en el timebox → activar Plan B (SERIALIZABLE sin retry) y registrarlo
- [x] **Task 2 — Spike del pipeline del mediator (AC: 2)**
  - [x] `IRequestHandler<TRequest,TResponse>` con `Task<TResponse> Handle(TRequest, CancellationToken)`, `TResponse = Result/Result<T>`
  - [x] Behaviors como decorators `IPipelineBehavior<,>`: `Logging → Validation → Transaction → Outbox → Handler`
  - [x] Comando trivial que escribe una fila de dominio + una fila `OutboxMessages` en el **mismo** `SaveChangesAsync`
  - [x] Test que verifica orden de composición y atomicidad (dominio + outbox en la misma tx)
- [x] **Task 3 — Destilar el aprendizaje (AC: 3)**
  - [x] Guardar snippets de referencia (clasificación de `SqlException`, `TransactionBehavior` con retry 1205, registro `AddMediatorPipeline`) en un doc consumible (p. ej. `docs/implementation-artifacts/spikes-referencia.md`)
  - [x] Registrar la decisión go/no-go (y Plan B si aplica)
- [x] **Task 4 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Qué se valida y por qué

El `architecture.md` declara **"diseño completo, ejecución NO validada"**. Estos dos spikes retiran la incertidumbre empírica antes del core. Son **desechables a propósito** (fuera del código productivo) para no contaminar la regla de "100% verde" con throwaway. Fuente: [architecture.md — Architecture Readiness Assessment](../planning-artifacts/architecture.md).

### Reglas de arbitraje (fuente `concurrency-and-messaging.md` / ADR-016)

- INSERT bajo **READ COMMITTED** (NO `SERIALIZABLE`); el `UNIQUE(HabitacionId, Noche)` es el árbitro.
- `2627`/`2601` → **409 inmediato, cero retry** (determinístico: otro ganó).
- `1205` (deadlock victim) → **retry acotado** (3 intentos, backoff+jitter); re-ejecuta el handler completo (idempotente). Sin SERIALIZABLE se esperan *más* 1205 — es nominal.
- Slots del batch en orden determinístico `ORDER BY HabitacionId, Noche`.
- **Clasificación por `SqlException.Number`, NUNCA por `.Message`** (anti-patrón explícito).

### Contrato del mediator (fuente ADR-018 / `architecture.md#Contrato del mediator`)

- Firma: `Task<TResponse> Handle(TRequest request, CancellationToken ct)`; `TResponse = Result`/`Result<T>` (los flujos esperados no lanzan; 409/400/403 son `Result`).
- Pipeline por **decorators** `IPipelineBehavior<TRequest,TResponse>` (composición anidada, no hardcode), orden `Logging → Validation → Transaction → Outbox → Handler`.
- Registro por scan de assembly en un único `AddMediatorPipeline()`; `TransactionBehavior` **solo comandos** (las queries no).
- **Regla no negociable:** el insert a `OutboxMessages` va en el mismo `DbContext.SaveChangesAsync()` que el cambio de dominio; el `TransactionBehavior` envuelve ambos y asigna el `MessageId` **una vez antes** del retry 1205.

### Límites de alcance

- Es un spike: NO construyas el mediator ni el schema definitivos (eso es 1.4/1.5/1.6). Aquí solo **pruebas que el enfoque funciona** y dejas snippets.
- No requiere cobertura ≥80% ni pasar por el pipeline de calidad productivo.

### Anti-patrones a evitar

- Parsear `SqlException.Message` para clasificar.
- Regenerar el `MessageId` dentro del loop de retry.
- Convertir el spike en código productivo "porque quedó bien" — su destino es documentar y morir.

### Testing

- Testcontainers.MsSql (SQL Server real) — imprescindible: la concurrencia y la unicidad NO se prueban con InMemory.
- Ejecución aislada para el test de concurrencia (evitar interferencia).

### Git

- Commit + push a `develop` por cambio cerrado; Conventional Commits en español; autor **Santiago Renteria**, sin coautoría IA.

### Project Structure Notes

- El código del spike puede vivir en una carpeta/rama de spike; el único artefacto persistente es el doc de snippets (`docs/implementation-artifacts/spikes-referencia.md`).

### References

- [epics.md — Story 1.2](../planning-artifacts/epics.md) (AC-E1.2.1…3).
- [architecture.md — Architecture Readiness Assessment / Contrato del mediator ADR-018](../planning-artifacts/architecture.md).
- [concurrency-and-messaging.md](../specs/spec-hotel-booking-hub/concurrency-and-messaging.md) (READ COMMITTED, clasificación 2627/2601 vs 1205).
- [decisions-adr.md](../specs/spec-hotel-booking-hub/decisions-adr.md) (ADR-016, ADR-018).

## Dev Agent Record

### Agent Model Used

Claude Opus 4.8 (claude-opus-4-8) vía bmad-dev-story.

### Debug Log References

- Spike ejecutado fuera del repo (scratchpad, throwaway). `dotnet test` → **3/3 Passed**:
  - `ArbitrajeSpike.Indice_unico_arbitra_N_inserciones_concurrentes` → `N=50 | confirmadas=1 | conflicto-unico(2627/2601)=49 | deadlock(1205)=0 | otros=0` (Testcontainers.MsSql, SQL Server 2022 real).
  - `MediatorSpike.Pipeline_compone_en_orden_canonico` → `orden = Logging → Validation → Transaction → Outbox → Handler`.
  - `MediatorSpike.Dominio_y_outbox_comparten_un_solo_SaveChanges` → `filas afectadas en un SaveChanges = 2`; rollback → sin residuo.

### Completion Notes List

**Decisión: GO en ambos spikes (sin Plan B).** El código del spike era desechable y se eliminó; el aprendizaje persiste en [spikes-referencia.md](spikes-referencia.md).
- **AC-E1.2.1 ✅** — el índice `UNIQUE(HabitacionId, Noche)` arbitra bajo READ COMMITTED; clasificación por `SqlException.Number` (2627/2601 vs 1205) validada; cero overbooking (1 fila). No hace falta `SERIALIZABLE`.
- **AC-E1.2.2 ✅** — pipeline compone en orden canónico; dominio + outbox en un solo `SaveChangesAsync` (atómico, rollback todo-o-nada).
- **AC-E1.2.3 ✅** — snippets de referencia + decisión go/no-go en `spikes-referencia.md` (alimentan 1.5 y 1.6b).
- Nota: 0×1205 en la corrida (inserción de fila única → gana por PK 2627, nominal); la ruta de retry de 1205 queda codificada/reservada para el patrón batch de 1.5/1.6c.
- Sin impacto en la solución ni en el CI (spike fuera del repo). `dotnet build`/`test` del repo intactos.

### File List

- `docs/implementation-artifacts/spikes-referencia.md` (nuevo — artefacto persistente del spike)
- *(código del spike: throwaway, fuera del repo, eliminado — no versionado)*

### Change Log

- 2026-07-08 · Story 1.2 · spikes de Sprint 0 ejecutados (Testcontainers.MsSql + SQLite): arbitraje 2627/1205 y wiring del mediator validados empíricamente. **GO** en ambos, sin Plan B. Aprendizaje destilado en `spikes-referencia.md`; código throwaway eliminado. Estado: `in-progress` → `review`.
