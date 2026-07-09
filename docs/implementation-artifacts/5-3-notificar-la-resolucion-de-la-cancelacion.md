---
baseline_commit: 763121ca100292ad4ac730f4305553593df64c3e
---

# Story 5.3: Notificar la resolución de la cancelación

Status: in-progress

<!-- Generado por bmad-create-story (lote Épica 5). Complejidad NORMAL. Consume DOS eventos de resolución
(ReservaCancelada.v1 y SolicitudCancelacionRechazada.v1, definidos/probados en 4.2/4.3). Reutiliza INotificador
(5.1a) + inbox idempotente (5.1b). Cierra la Épica 5. TDD Red→Green. -->

## Story

Como **viajero**,
quiero **recibir el desenlace de mi solicitud de cancelación**,
para **saber la penalidad final, si fue condonada, o que mi reserva sigue en pie**.

## Acceptance Criteria

1. **AC-E5.3.1 — Aprobación / condonación.**
   **Dado** una `ReservaCancelada`,
   **cuando** el worker la consume,
   **entonces** el viajero recibe la penalidad **final** (con nota del agente si difiere de la sugerida) o el aviso de **condonación**.
2. **AC-E5.3.2 — Rechazo (mensaje inequívoco).**
   **Dado** una `SolicitudCancelacionRechazada`,
   **cuando** el worker la consume,
   **entonces** el viajero recibe un correo indicando que la reserva **sigue `Confirmada`** y el **motivo del rechazo**.

## Tasks / Subtasks

- [ ] **Task 1 — Handler de `ReservaCancelada.v1` (AC: 1)** *(TDD)*
  - [ ] Deserializar (`ReservaCanceladaV1`: `AggregateId`=ReservaId, `ResueltaPor`, `FechaResolucion`, `PenalidadAplicadaPorcentaje`, `PenalidadFueOverride`). Correo al viajero con la penalidad **final aplicada**; si `PenalidadFueOverride` (difiere de la sugerida) → indicarlo (p. ej. condonación cuando aplicada = 0, o ajuste del agente).
- [ ] **Task 2 — Handler de `SolicitudCancelacionRechazada.v1` (AC: 2)** *(TDD)*
  - [ ] Deserializar (`SolicitudCancelacionRechazadaV1`: `AggregateId`, `ResueltaPor`, `FechaResolucion`, `MotivoRechazo`). Correo al viajero: la reserva **sigue Confirmada** + el **motivo del rechazo**. Mensaje inequívoco (no ambiguo con una cancelación).
- [ ] **Task 3 — Idempotencia + reutilización (AC: 1, 2)**
  - [ ] Ambos handlers pasan por `IInboxIdempotencia` (5.1b) — dedup por `(MessageId, version)` — y `INotificador` (5.1a). **Rechazo NUNCA comunica cancelación** y viceversa (invariante del contrato de eventos de 4.2).
- [ ] **Task 4 — Tests (unit + integración)**
  - [ ] Unit: aprobación aplicando → penalidad final; condonación (`FueOverride` + 0%) → aviso de condonación; rechazo → "sigue Confirmada" + motivo. Idempotencia (N entregas → 1 efecto). Que un evento de rechazo no dispare copy de cancelación.
- [ ] **Task 5 — Commits TDD (Red→Green) en rama `feature/5-3-notificar-resolucion` + PR a `develop`** (autor Santiago Renteria; sin trailers). Cierra la Épica 5.

## Dev Notes

### Arquitectura y archivos a tocar

- **Worker:** dos handlers nuevos (`ReservaCanceladaV1`, `SolicitudCancelacionRechazadaV1`, ambos en `Comun.Eventos`, definidos/probados en 4.2 y consumidos por primera vez aquí). Reutiliza `INotificador` + `IInboxIdempotencia`.
- **Datos del destinatario:** mismo gap que 5.2 — los eventos de resolución no traen el email del viajero. Resolver con la MISMA decisión que 5.2 (enriquecer aditivo/versionado, o proyección ReservaId→emails alimentada por `ReservaConfirmada.v1`). Ser consistente entre 5.2 y 5.3.
- **Penalidad final vs sugerida:** `PenalidadAplicadaPorcentaje` es la EFECTIVA (4.2); `PenalidadFueOverride` indica si el agente se apartó de la sugerida congelada (condonar/ajustar).

### Previous story intelligence

- 4.2 define/prueba `ReservaCancelada.v1` y `SolicitudCancelacionRechazada.v1` (order key = ReservaId; "rechazar NUNCA emite ReservaCancelada").
- 5.2 resolvió el copy "estimación"; 5.3 comunica el desenlace **final** — no confundir ambos.
- 5.1a/5.1b dejan `INotificador` + inbox idempotente.

### Anti-patrones a evitar

- Comunicar penalidad final como estimación (o viceversa; 5.2 = estimación, 5.3 = final).
- Que un rechazo dispare copy de cancelación (o al revés) — respetar el desenlace del evento.
- Duplicar el efecto (reusar el inbox idempotente).

### References

- [epics.md — Story 5.3 (AC-E5.3.1/.2)](../planning-artifacts/epics.md)
- [Story 4.2 — ReservaCancelada.v1 / SolicitudCancelacionRechazada.v1](4-2-resolver-cancelacion-con-auditoria.md)
- [Story 5.2 — copy de estimación + gap de destinatario](5-2-notificar-la-solicitud-de-cancelacion.md)

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List

### Change Log
