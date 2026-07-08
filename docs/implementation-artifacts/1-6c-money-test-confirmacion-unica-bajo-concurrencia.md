# Story 1.6c: Money test — confirmación única bajo concurrencia

Status: ready-for-dev

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

- [ ] **Task 1 — `ReservaTestDataBuilder` / seed determinista (AC: 1)**
  - [ ] Builder en `TestKit` que siembra 1 `Hotel` + 1 `Habitacion` + 1 noche disponible, reproducible
  - [ ] Semilla parametrizable; el N (rango 30–100) se sortea y se registra en la salida del test
- [ ] **Task 2 — Arnés de concurrencia (AC: 2, 3)**
  - [ ] Lanzar N `CrearReservaCommand` concurrentes reales sobre el mismo slot (paralelismo verdadero, no secuencial)
  - [ ] Recolectar respuestas y clasificar: `201` vs `409` vs (defecto) excepción no mapeada
- [ ] **Task 3 — Aserciones exactas (AC: 2, 3)**
  - [ ] `confirmadas == 1`; `rechazadas == N-1` (número **exacto**, no `>= 1`)
  - [ ] `count(Reserva Confirmada) == 1`; `count(OutboxMessages) == 1`; `0` filas outbox para las rechazadas
  - [ ] `0` excepciones no mapeadas; `1205` agotado → `409` (nunca `500`); retries 1205 acotados a 3
  - [ ] Registrar N y semilla en la salida
- [ ] **Task 4 — Aislamiento de la collection (AC: 3)**
  - [ ] Collection xUnit `G1` con `[CollectionDefinition(DisableParallelization = true)]`, contenedor propio, reset por test (`Respawn`)
  - [ ] En CI corre como **stage secuencial aparte**, nunca en el `dotnet test` masivo paralelo
- [ ] **Task 5 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

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

### Debug Log References

### Completion Notes List

### File List
