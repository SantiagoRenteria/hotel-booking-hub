# Story 4.2: Resolver cancelación (aprobar / condonar / rechazar) con auditoría

Status: ready-for-dev

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

- [ ] **Task 1 — Transición de resolución en el dominio (AC: 1, 2, 3)** *(TDD + BDD)*
  - [ ] `Reserva.Resolver(decision, penalidadDecidida, agente)` con guard (solo desde `CancelacionSolicitada`): aprobar (aplicar/condonar) → `Cancelada` + liberar slots; rechazar → `Confirmada`. Tests de tabla de transiciones. Doble resolución → guard + `rowVersion`.
  - [ ] Liberación de inventario: borrar las `NochesHabitacion` de la estancia como parte del agregado (cascade ya existe; confirmar el borrado explícito y el guard contra doble liberación).
- [ ] **Task 2 — Penalidad decidida + auditoría (AC: 1)** *(TDD)*
  - [ ] Registrar `PenalidadDecidida` (flag default/override respecto a la sugerida congelada + quién decidió + cuándo). Persistir en la `Reserva`/solicitud. Migración si aplica.
- [ ] **Task 3 — Command + handler + eventos (AC: 1, 2, 3)** *(TDD Red→Green)*
  - [ ] `ResolverCancelacionCommand` + handler (`ICommand`, transaccional). Aprobar → `ReservaCancelada.v1`; rechazar → `SolicitudCancelacionRechazada.v1`. Ambos por outbox en la misma tx.
  - [ ] Aislamiento: agente ajeno → `403` (Result.Prohibido) vía `IContextoAgente` (patrón 3.3).
- [ ] **Task 4 — Contratos de eventos (contract test)**
  - [ ] `ReservaCancelada.v1` + `SolicitudCancelacionRechazada.v1` en `Comun.Eventos` + pin de contrato.
- [ ] **Task 5 — Endpoint (AC: 1, 2, 3, 4)**
  - [ ] `POST /api/v1/reservas/{id}/cancelaciones/resolucion` (o `PUT .../cancelacion`) en `Reservas.Api`; identidad del agente server-side; Result→HTTP.
- [ ] **Task 6 — Tests (unit + integración Testcontainers)**
  - [ ] **El assert que importa (AC-E4.2.1):** aprobar → nueva reserva sobre `[D]` responde 201 (round-trip real con el motor anti-overbooking de E1). Rechazar no toca slots. Doble resolución concurrente → 409 y `count(slots)` no sube a 2 (money-test style). Agente ajeno → 403.
- [ ] **Task 7 — Commits TDD (Red→Green) + BDD en rama `feature/4-2-resolver-cancelacion` + PR a `develop`** (autor Santiago Renteria; sin trailers)

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

### Debug Log References

### Completion Notes List

### File List

### Change Log
