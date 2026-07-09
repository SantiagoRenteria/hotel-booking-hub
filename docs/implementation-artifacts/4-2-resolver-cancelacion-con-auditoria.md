---
baseline_commit: e258f22508287b39e19f051613d0e7229d908f8a
---

# Story 4.2: Resolver cancelación (aprobar / condonar / rechazar) con auditoría

Status: review

<!-- Generado por bmad-create-story. Complejidad ALTA (liberación de inventario + concurrencia + auditoría +
aislamiento por agente). 2ª VITRINA BDD: BDD (Given/When/Then) + TDD Red→Green visible. El modelo de dominio y la
máquina de estados se fijan en la Task 0 de 4.1 (compartida por la épica) — 4.2 la extiende con la resolución. -->

## Story

Como **agente del hotel**,
quiero **resolver una solicitud de cancelación con discreción**,
para **aplicar la penalidad, condonarla o rechazar, liberando inventario cuando corresponde**.

El efecto de negocio que casi nadie prueba: **aprobar libera el slot** (DELETE de `NochesHabitacion`), verificado
con un round-trip real (una nueva reserva sobre esa noche vuelve a caber). Determinismo vs juicio del agente.

## Acceptance Criteria

1. **AC-E4.2.1 — Aprobar libera el slot (round-trip — el assert que importa).**
   **Dado** una reserva `CancelacionSolicitada` que ocupa el único slot de `[D]`,
   **cuando** el agente aprueba (aplicando o condonando),
   **entonces** la reserva pasa a `Cancelada`, se borran las `NochesHabitacion` de la estancia,
   `count(slots disponibles en [D]) == 1`, **y** una **nueva** `CrearReserva` sobre `[D]` responde `201`,
   **y** se registra la `PenalidadDecidida` (flag default/override + quién decidió) y se escribe `ReservaCancelada`
   en el outbox.
2. **AC-E4.2.2 — Rechazar no toca slots.** Dado una reserva `CancelacionSolicitada`, cuando el agente rechaza con
   motivo, **entonces** la reserva vuelve a `Confirmada`, no se libera ningún slot y se escribe
   `SolicitudCancelacionRechazada` en el outbox.
3. **AC-E4.2.3 — Doble resolución / doble liberación (AC negativo).** Dado una reserva ya resuelta, cuando llega
   una segunda resolución concurrente (mismo `rowVersion`), **entonces** `409` (guard de estado + `rowVersion`);
   `count(slots)` **no** sube a `2`.
4. **AC-E4.2.4 — Agente ajeno (AC negativo).** Dado un agente que no es dueño del hotel de la reserva, cuando
   intenta resolver, **entonces** `403`.

## Tasks / Subtasks

> **Nota:** el modelo de dominio (estados, penalidad, VO de solicitud, eventos) viene de la Task 0 party-mode de
> 4.1. Aquí solo se decide, si surge, el **aislamiento del agente resolutor** (AC-E4.2.4): coherente con 3.3
> (Opción c, `IContextoAgente` + `AgenteEmail`) — reusar, no reinventar. Si "dueño del hotel" difiere de
> "quién reservó", escalar a party-mode (misma tensión que la Task 0 de 3.3).

- [x] **Task 1 — Transición de resolución en el dominio (AC: 1, 2, 3)** *(TDD + BDD)*
  - [x] `Reserva.Resolver(decision, resueltaPor, fechaResolucion, motivoRechazo)` con guard (solo desde `CancelacionSolicitada`): aprobar (aplicar/condonar) → `Cancelada` + liberar slots; rechazar → `Confirmada`. Tests de tabla de transiciones. Doble resolución → guard (+`rowVersion` en Task 3/infra).
  - [x] Liberación de inventario: `_noches.Clear()` en el agregado (EF borra los huérfanos); guard contra doble liberación por el guard de estado.
- [x] **Task 2 — Penalidad decidida + auditoría (AC: 1)** *(TDD)*
  - [x] `SolicitudCancelacion` gana resolución (mismo episodio): `ResueltaPor`, `FechaResolucion`, `Resultado`, `PenalidadAplicadaPorcentaje`, `PenalidadFueOverride` (default/override vs la sugerida congelada), `MotivoResolucion`. Migración en Task 3/infra.
- [x] **Task 3 — Command + handler + eventos (AC: 1, 2, 3)** *(TDD Red→Green)*
  - [x] `ResolverCancelacionCommand` + handler (`ICommand`, transaccional). Aprobar → `ReservaCancelada.v1`; rechazar → `SolicitudCancelacionRechazada.v1`. Ambos por outbox en la misma tx. Concurrencia optimista → 409 (`EjecutorTransaccional` traduce `DbUpdateConcurrencyException`).
  - [x] Aislamiento: agente ajeno / sin identidad → `403` (Result.Prohibido) vía `IContextoAgente` (patrón 3.3).
- [x] **Task 4 — Contratos de eventos (contract test)**
  - [x] `ReservaCancelada.v1` + `SolicitudCancelacionRechazada.v1` en `Comun.Eventos` + `ContratoEventosResolucionCancelacionTests`.
- [x] **Task 5 — Endpoint (AC: 1, 2, 3, 4)**
  - [x] `POST /api/v1/reservas/{id}/cancelacion/resolucion` en `Reservas.Api`; identidad del agente server-side; Result→HTTP.
- [x] **Task 6 — Tests (unit + integración Testcontainers)**
  - [x] **El assert que importa (AC-E4.2.1):** aprobar → nueva reserva sobre `[D]` responde 201 (round-trip real con el motor anti-overbooking de E1). Rechazar no toca slots. Doble resolución concurrente → 1×2xx/1×409 y `count(slots)` no sube a 2 (money-test style). Agente ajeno → 403.
- [x] **Task 7 — Commits TDD (Red→Green) + BDD en rama `feature/4-2-resolver-cancelacion` + PR a `develop`** (autor Santiago Renteria; sin trailers)

### Review Findings

<!-- Code review adversarial 2026-07-09. Capa Blind Hunter fallida (salida inservible); Edge + Auditor completas.
Veredicto Auditor: cumplimiento completo de AC-E4.2.1/.2/.3/.4. -->

- [x] [Review][Patch] `Reserva.Resolver` (aprobar) hace `_noches.Clear()` solo si el llamador cargó las Noches; método público sin guard → fuga silenciosa de inventario si un futuro llamador usa `ObtenerAsync` (sin Include) [Reserva.cs] — ✅ guard: aprobar con `_noches` vacía lanza `InvalidOperationException` (falla fuerte en vez de dejar inventario fantasma).
- [x] [Review][Patch] `MotivoRechazo` reusado como nota de aprobación (naming confuso) [Reserva.cs] — ✅ parámetro de dominio renombrado a `motivo` (genérico).
- [x] [Review][Defer] Re-solicitar cancelación tras un rechazo sobrescribe la auditoría del episodio rechazado en la owned `SolicitudCancelacion` [Reserva.cs] — deferido: es el diseño **single-episode** de Task 0 ("un solo episodio, no dos owned"); el audit **durable** del rechazo es el evento `SolicitudCancelacionRechazada.v1` ya emitido al outbox. Si se requiere historial en BD, es una historia propia (tabla de episodios).
- [x] [Review][Defer] Reserva con `AgenteEmail` null queda irresoluble (403 permanente) [ResolverCancelacionCommandHandler.cs] — deferido; fail-closed correcto, consistente con el deferido de 3.3 (backfill de `AgenteEmail`). En producción 3.3 siempre setea el agente.
- [x] [Review][Defer] Sin test a nivel HTTP del endpoint de resolución (200/400/409/403 + binding) [ResolverCancelacionTests.cs] — deferido; los tests usan `ISender` (patrón del repo). Misma deuda de wiring HTTP transversal ya registrada.

## Dev Notes

### Arquitectura y archivos a tocar

- **Dominio:** `Reserva.Resolver(...)` con guard de estado; liberación de slots (`NochesHabitacion`). Reusar el motor anti-overbooking (E1): la unicidad de `(HabitacionId, Noche)` garantiza que tras liberar, una nueva reserva cabe. [Source: NocheHabitacion.cs, AntiOverbookingTests]
- **Concurrencia (AC-E4.2.3):** guard de estado + `rowVersion` (optimistic) → 409; probar con dos resoluciones concurrentes (patrón money-test G1 de 1.6c) que `count(slots)` no sube a 2.
- **Aislamiento (AC-E4.2.4):** `IContextoAgente` (3.3) para el agente resolutor; reserva ajena → 403.
- **Eventos:** `ReservaCancelada.v1`, `SolicitudCancelacionRechazada.v1` (E4 los define/prueba; E5 consumirá).
- **Escritura:** `EjecutorTransaccional` + outbox en una tx.

### Previous story intelligence

- **AC-E4.2.1 es el assert de negocio clave** — no basta con cambiar el estado; el slot debe quedar realmente libre (round-trip 201). Reutilizar `ReservaTestDataBuilder`/helpers de integración de E1.
- El money-test G1 (1.6c) es el patrón para la doble liberación concurrente.
- `AgenteEmail` de 3.3 + `IContextoAgente` para el 403.

### Anti-patrones a evitar

- Aprobar sin borrar los slots (inventario fantasma; rompe AC-E4.2.1).
- Doble liberación (borrar slots dos veces / permitir segunda resolución) — guard + rowVersion.
- Recalcular penalidad (usar la congelada; aquí solo se decide default/override).
- Resolver una reserva de otro agente (fuga; 403).

### References

- [epics.md — Story 4.2 (AC-E4.2.x)](../planning-artifacts/epics.md)
- [Story 4.1](4-1-solicitar-cancelacion-con-politica-sugerida.md) (Task 0: modelo de dominio de cancelación)
- [1.6c money-test](1-6c-money-test-confirmacion-unica-bajo-concurrencia.md) (patrón de concurrencia)

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Amelia, dev-story) — 2026-07-09.

### Debug Log References

- Suite completa verde: 329 tests, 0 fallos (Reservas.UnitTests 132, Reservas.IntegrationTests 42, Contracts 21, Comun.Web 15, Hoteles 100+19).
- TDD Red→Green visible en commits (`test(4.2): RED …` → `feat(4.2): GREEN …`) para dominio y handler; BDD en dominio e integración.

### Completion Notes List

- **AC-E4.2.1 (el assert que importa):** aprobar → `Cancelada` + `_noches.Clear()` (EF borra los huérfanos) → el slot queda libre; verificado con round-trip real (una nueva `CrearReserva` sobre la noche liberada responde 201).
- **Aprobar aplicando vs condonando:** la penalidad NUNCA se recalcula; aplicar usa la sugerida congelada (4.1), condonar la fija en 0. `PenalidadFueOverride` = aplicada ≠ congelada. Auditoría (`ResueltaPor`/`FechaResolucion`/`Resultado`/`MotivoResolucion`) en la MISMA owned `SolicitudCancelacion`.
- **AC-E4.2.2:** rechazar (con motivo) → vuelve a `Confirmada` sin tocar slots; evento `SolicitudCancelacionRechazada.v1`.
- **AC-E4.2.3 (doble liberación):** el borrado de slots va en la MISMA tx que el bump de `rowVersion`; la 2ª resolución concurrente choca con `DbUpdateConcurrencyException`, que `EjecutorTransaccional` traduce a `ConflictoConcurrenciaException` → 409 (sin retry). Verificado: 1×2xx/1×409, `count(slots)`=0 (no sube a 2), exactamente 1 `ReservaCancelada`. **Cierra el diferido de concurrencia de 4.1.**
- **AC-E4.2.4:** aislamiento por `AgenteEmail` (patrón 3.3, `IContextoAgente` fail-closed): agente ajeno / sin identidad → 403. Decisión de diseño: en este código la única identidad de agente es la del que reservó; no existe "dueño del hotel" separado → no requirió party-mode.
- **Eventos** `ReservaCancelada.v1` y `SolicitudCancelacionRechazada.v1` (order key = `ReservaId`) con contract test propio; un evento por comando, en la tx del agregado.

### File List

**Dominio (Reservas.Domain):**
- `Reservas/DecisionCancelacion.cs` (A), `Reservas/ResultadoResolucion.cs` (A)
- `Reservas/SolicitudCancelacion.cs` (M — resolución: RegistrarAprobacion/RegistrarRechazo)
- `Reservas/Reserva.cs` (M — `Resolver` + liberación de slots)
- `Puertos/IReservaRepository.cs` (M — `ObtenerConNochesAsync`)

**Aplicación (Reservas.Application):**
- `Reservas/ResolverCancelacion/ResolverCancelacionCommand.cs` (A — command + response DTO + request HTTP)
- `Reservas/ResolverCancelacion/ResolverCancelacionCommandHandler.cs` (A)
- `Reservas/ResolverCancelacion/ResolverCancelacionCommandValidator.cs` (A)

**Comun:** `Eventos/ReservaCanceladaV1.cs` (A), `Eventos/SolicitudCancelacionRechazadaV1.cs` (A)

**Infraestructura (Reservas.Infrastructure):**
- `Persistencia/ReservasDbContext.cs` (M — mapeo de resolución)
- `Persistencia/ReservaRepository.cs` (M — `ObtenerConNochesAsync`)
- `Persistencia/EjecutorTransaccional.cs` (M — `DbUpdateConcurrencyException` → 409)
- `Migraciones/20260709185445_AgregaResolucionCancelacion.cs` (+ Designer + snapshot) (A/M)

**Api:** `Reservas.Api/Program.cs` (M — endpoint de resolución)

**Tests:**
- `Reservas.UnitTests/Dominio/ReservaResolucionTests.cs` (A)
- `Reservas.UnitTests/Reservas/ResolverCancelacion/{ResolverCancelacionCommandHandlerTests,ResolverCancelacionCommandValidatorTests}.cs` (A)
- `Reservas.UnitTests/Reservas/CrearReserva/Fakes.cs` (M — `ObtenerConNochesAsync`)
- `Contracts/ContratoEventosResolucionCancelacionTests.cs` (A)
- `Reservas.IntegrationTests/ResolverCancelacionTests.cs` (A)

### Change Log

- 2026-07-09 — Story 4.2 implementada (dominio + aplicación + infraestructura + api + tests). Estado → review.
