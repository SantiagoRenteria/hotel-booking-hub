---
baseline_commit: 4dc2564
---

# Story 1.6c: Money test — confirmación única bajo concurrencia

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

Como **operador del sistema**,
quiero **que N reservas concurrentes sobre la misma habitación/fechas produzcan exactamente una confirmada**,
para **garantizar cero overbooking bajo carga**.

> **El flujo crítico — la prueba de la promesa central.** La historia más cara (Testcontainers.MsSql + paralelismo real), aislada para que un deadlock intermitente no bloquee otras. **Depende de 1.4 + 1.5 + 1.6b** (precio, slots/arbitraje, atomicidad del outbox). Cierra `AC-E1` (productor). NO toca el consumidor (E5) ni la proyección (E3).

## Acceptance Criteria

1. **AC-E1.6c.1 — Seed determinista (habilitador).** Dado un `ReservaTestDataBuilder` (ObjectMother), cuando prepara el escenario, entonces crea 1 `Hotel`, 1 `Habitacion` y 1 noche disponible de forma reproducible (mismo estado en cada corrida).
2. **AC-E1.6c.2 — Confirmación única bajo concurrencia.** Dado el seed anterior (un único slot libre para la estancia `[D]`), cuando se ejecutan N solicitudes `CrearReservaCommand` concurrentes sobre ese slot, entonces se cumple exactamente:

   | N | confirmadas (`201`) | rechazadas (`409`) | filas `Reserva` `Confirmada` | filas `OutboxMessages` |
   |---|---|---|---|---|
   | 2 | 1 | 1 | 1 | 1 |
   | 50 | 1 | 49 | 1 | 1 |

   Y no existe fila en `OutboxMessages` para ninguna de las reservas rechazadas.

3. **AC-E1.6c.3 — Determinismo (sin flakiness).** Dado la corrida del money test, cuando se clasifican las respuestas, entonces hay `exactamente 1` × `201` y `N-1` × `409`; `0` excepciones no mapeadas; los reintentos por deadlock `1205` están acotados (máx. 3, backoff+jitter) y un `1205` agotado se mapea a `409`, nunca a `500`. *(Collection `G1` aislada, `DisableParallelization = true`; el N sorteado 30–100 y la semilla se registran en la salida para reproducibilidad.)*

## Tasks / Subtasks

- [x] **Task 1 — `ReservaTestDataBuilder` / seed determinista (AC: 1)**
  - [x] `ReservaTestDataBuilder` (ObjectMother) que produce un `CrearReservaCommand` válido reproducible para una habitación + 1 noche (la disponibilidad sembrada trata cualquier habitación como activa; no hay tablas Hotel/Habitacion hasta E2)
  - [x] N (rango 30–100) sorteado con semilla registrada en la salida del test (`ITestOutputHelper`)
- [x] **Task 2 — Arnés de concurrencia (AC: 2, 3)**
  - [x] N `CrearReservaCommand` concurrentes REALES (`Task.WhenAll`, un scope DI por comando) sobre el mismo slot, por el pipeline de producción (mediator → Validation → Transaction → handler → EjecutorTransaccional → SQL)
  - [x] Clasifica: `201` (Result.Ok) vs `409` (`HabitacionNoDisponibleException`) vs `Inesperado` (excepción no mapeada / Result inesperado)
- [x] **Task 3 — Aserciones exactas (AC: 2, 3)**
  - [x] `confirmadas == 1`; `rechazadas == N-1` (exacto)
  - [x] `count(Reserva Confirmada) == 1`; `count(OutboxMessages) == 1`; 0 filas outbox para las rechazadas (rollback)
  - [x] `0` excepciones no mapeadas; `1205` agotado → `409` (mapeo añadido en `EjecutorTransaccional`, nunca `500`); retries 1205 acotados a 3 (de 1.5)
  - [x] N y semilla registrados en la salida
- [x] **Task 4 — Aislamiento de la collection (AC: 3)**
  - [x] Collection xUnit `G1` con `[CollectionDefinition("G1", DisableParallelization = true)]`, contenedor propio; reset por test vía `HelpersOutbox.LimpiarAsync` (en vez de `Respawn` para no añadir dependencia)
  - [x] CI: stage secuencial aparte (`--filter "Category=G1"`); el pool paralelo corre `--filter "Category!=G1"`
- [x] **Task 5 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Qué prueba y qué NO (fuente `architecture.md#Architecture Readiness` / party-mode: Murat)

- Prueba el **invariante anti-overbooking + atomicidad del productor** bajo concurrencia real. Es el "money test G1" del enunciado (G1: N concurrentes → 1 confirmada, resto 409).
- **NO** prueba exactly-once end-to-end. La idempotencia del consumidor (E5) y la convergencia de la proyección (E3) son deuda ya escrita en 1.6b (`[DEUDA-VERIF:E5/E3]`). No incluyas al consumidor aquí — si el test toca el consumidor, `deliveries >= 1` se convierte en `efectos == 1` y borras la evidencia de haber probado al productor aislado.

### Determinismo (crítico para no marcar el test `[Flaky]`)

- Umbral explícito: `exactamente 1×201`, `N-1×409`, `0` excepciones no mapeadas.
- Un `1205` que agota reintentos cuenta como **fallo esperado → 409**, no como flake ni `500`.
- Retries 1205 acotados (3×, backoff+jitter). Slots insertados en orden determinístico (de 1.5).

### Límites de alcance

- NO consumidor/idempotencia (E5). NO proyección (E3). NO búsqueda (E3). Reutiliza `CrearReserva` (1.6a), el arbitraje (1.5) y la atomicidad+relay (1.6b) tal cual — no los reimplementes.

### Anti-patrones a evitar

- `>= 1` en vez de conteo **exacto** de confirmadas/rechazadas.
- Concurrencia "simulada" secuencial (debe ser paralelismo real).
- Correr `G1` en el pool paralelo masivo (debe ir aislada, `DisableParallelization=true`, stage aparte).
- Tocar el consumidor para "adelantar" idempotencia.
- Probar con EF InMemory (NO sirve — Testcontainers.MsSql).

### Testing (fuente `architecture.md#Estrategia de tests`)

- Collection `G1` aislada con `DisableParallelization=true`, contenedor propio, `Respawn` por test. `TestKit` provee fixtures (imagen pineada) y el `ReservaTestDataBuilder`.
- CI: stage secuencial separado (bloqueante en PR a `main`).

### Previous story intelligence (plan-based)

- De 1.6b: relay + `OutboxMessages` + atomicidad ya existen; el money test verifica la fila única de outbox tras la ráfaga. De 1.5: arbitraje 2627/1205 y orden determinístico. De 1.6a: `CrearReservaCommand` + endpoint. (Actualizar con patrones reales tras implementarlas.)

### Git

- Commit + push a `develop` por cambio cerrado; autor **Santiago Renteria**, sin coautoría IA.

### Project Structure Notes

- Test en `tests/Reservas.IntegrationTests/` (collection `G1`). Builder/fixtures en `tests/TestKit/`. Config de CI en `.github/workflows/ci.yml` (stage secuencial).

### References

- [epics.md — Story 1.6c](../planning-artifacts/epics.md) (AC-E1.6c.1…3).
- [architecture.md — Architecture Readiness Assessment (money test G1) / Estrategia de tests](../planning-artifacts/architecture.md).
- [concurrency-and-messaging.md](../specs/spec-hotel-booking-hub/concurrency-and-messaging.md) (arbitraje, retry).
- Stories 1.4 (precio), 1.5 (slots/arbitraje), 1.6a (comando), 1.6b (atomicidad/relay).

## Dev Agent Record

### Agent Model Used

Claude Opus 4.8 (claude-opus-4-8) vía bmad-dev-story.

### Debug Log References

- `dotnet build` 0/0; `dotnet format --verify-no-changes` limpio.
- **Money test G1 3/3** (Testcontainers.MsSql, contenedor propio): `N=2`, `N=50`, y fuzzeado `N∈[30,100]` con semilla registrada. Partición CI verificada: `Category!=G1` → Contracts 2/2 + UnitTests 52/52 + IntegrationTests no-G1 8/8; `Category=G1` → 3/3.
- Concurrencia REAL vía `Task.WhenAll` con un scope DI por comando sobre el pipeline de producción; el árbitro `UNIQUE(HabitacionId, Noche)` da 1 ganador + N-1 perdedores (2627 → `HabitacionNoDisponibleException`). El bloqueo de clave hace que los perdedores esperen al commit del ganador y reciban 2627 (no 1205) → determinista.
- **AC-E1.6c.3:** añadido en `EjecutorTransaccional` el mapeo de deadlock 1205 AGOTADO (tras 3 reintentos) → `HabitacionNoDisponibleException` (409), para garantizar "nunca 500". Cambio aditivo sobre 1.6b (no altera 2627→409 ni el camino feliz); no requirió party-mode (comportamiento prescrito por el AC, sin fork de diseño).

### Completion Notes List

- **AC-E1.6c.1 ✅** — `ReservaTestDataBuilder` (ObjectMother) produce un comando válido reproducible; N sorteado 30–100 con semilla en la salida.
- **AC-E1.6c.2 ✅** — tabla exacta verificada: `N=2 → 1/1`, `N=50 → 1/49`; `count(Reserva Confirmada)==1`, `count(OutboxMessages)==1`, 0 filas outbox para las rechazadas.
- **AC-E1.6c.3 ✅** — exactamente `1×201`, `N-1×409`, `0` excepciones no mapeadas; 1205 agotado → 409 (nunca 500); retries 1205 acotados a 3 (de 1.5). Collection `G1` aislada (`DisableParallelization=true`, contenedor propio) + stage CI secuencial aparte.
- Reutiliza sin reimplementar: `CrearReserva` (1.6a), arbitraje (1.5), atomicidad+relay (1.6b). NO toca consumidor (E5) ni proyección (E3) — la deuda `[DEUDA-VERIF:E5/E3]` sigue en 1.6b.
- **Cierra AC-E1 (productor).** Épica 1 completa: fundación ejecutable + anti-overbooking probado bajo concurrencia real.

### File List

- `src/Servicios/Reservas/Reservas.Infrastructure/Persistencia/EjecutorTransaccional.cs` (1205 agotado → 409)
- `.github/workflows/ci.yml` (partición `Category!=G1` paralelo + `Category=G1` secuencial)
- `tests/Reservas.IntegrationTests/` — `SqlServerFixture.cs` (+`CadenaConexion`), `ReservaTestDataBuilder.cs`, `MoneyTestG1Tests.cs` (+ collection `G1`) (nuevos)

### Change Log

- 2026-07-08 · Story 1.6c · money test G1: arnés de concurrencia real (N `CrearReservaCommand` vía `Task.WhenAll`, scope DI por comando) sobre el pipeline de producción + `ReservaTestDataBuilder` + collection `G1` aislada + partición CI. `EjecutorTransaccional`: 1205 agotado → 409. Money test 3/3 (N=2/50/fuzz 30–100). Estado: `ready-for-dev` → `in-progress` → `review`.
