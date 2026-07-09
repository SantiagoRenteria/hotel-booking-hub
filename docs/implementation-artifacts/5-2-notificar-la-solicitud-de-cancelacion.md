---
baseline_commit: b2a429d1540d9fdb20bbfa59bc33ba222e26d00d
---

# Story 5.2: Notificar la solicitud de cancelación

Status: in-progress

<!-- Generado por bmad-create-story (lote Épica 5). Complejidad NORMAL. Consume SolicitudCancelacionRegistrada.v1
(definido y probado por 4.1). Reutiliza INotificador (5.1a) + inbox idempotente (5.1b). TDD Red→Green. -->

## Story

Como **viajero y agente**,
quiero **ser avisado cuando se solicita una cancelación**,
para **conocer la penalidad estimada y (el agente) saber que hay algo por resolver**.

## Acceptance Criteria

1. **AC-E5.2.1 — Acuse con estimación.**
   **Dado** una `SolicitudCancelacionRegistrada`,
   **cuando** el worker la consume,
   **entonces** el **viajero** recibe un acuse que **incluye** la penalidad **estimada**, etiquetada explícitamente como estimación (no cobro final); el **agente** recibe aviso de "por resolver".

## Tasks / Subtasks

- [ ] **Task 1 — Handler del evento `SolicitudCancelacionRegistrada.v1` (AC: 1)** *(TDD)*
  - [ ] Deserializar el evento (`SolicitudCancelacionRegistradaV1`: `AggregateId`=ReservaId, `Iniciador`, `MotivoCategoria`, `MotivoDetalle`, `PenalidadPorcentaje`, `FechaSolicitud`). Emitir acuse al viajero con la penalidad **etiquetada como ESTIMACIÓN** (no cobro) y aviso "por resolver" al agente.
  - [ ] Reutilizar `INotificador` (5.1a) y el inbox idempotente (5.1b) — dedup por `(MessageId, version)`.
- [ ] **Task 2 — Copy inequívoco (AC: 1)**
  - [ ] El texto del correo del viajero deja claro que la penalidad es **estimada/sugerida** (congelada en la fecha de solicitud), no el cobro final (que llega en 5.3). El del agente indica que hay una solicitud "por resolver".
- [ ] **Task 3 — Tests (unit + integración)**
  - [ ] Unit: el handler emite acuse al viajero (incluye % estimado + etiqueta "estimación") y aviso al agente. Idempotencia: N entregas → 1 efecto por destinatario. Integración según transporte.
- [ ] **Task 4 — Commits TDD (Red→Green) en rama `feature/5-2-notificar-solicitud-cancelacion` + PR a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Arquitectura y archivos a tocar

- **Worker:** nuevo handler para `SolicitudCancelacionRegistradaV1` (`Comun.Eventos`, definido/probado en 4.1). Reutiliza `INotificador` + `IInboxIdempotencia` de 5.1a/5.1b.
- **Datos del correo:** el evento NO trae el email del viajero ni del agente directamente (lleva `Iniciador`, motivo, penalidad, fecha).
- ✅ **DECISIÓN (party-mode 5.2, unánime Winston/Amelia/John/Murat, aprobada por delegación de Santiago 2026-07-09): Opción (a)** — enriquecer `SolicitudCancelacionRegistrada.v1` (y en 5-3, `ReservaCancelada.v1`/`SolicitudCancelacionRechazada.v1`) de forma **ADITIVA** con `HuespedEmail` + `AgenteEmail`, poblados por el emisor de Reservas (4.1/4.2). Razones: (1) menor riesgo runtime (evento autocontenido, worker stateless, verificable en CI) vs la proyección local (b) que sufriría **expiración de TTL en cancelaciones tardías** —el caso común— y un **invariante inter-evento que ningún contract test cubre**; (2) E5 es el **primer y único consumidor** de estos eventos → el cambio aditivo no rompe a nadie; (3) consistencia con el precedente de `ReservaConfirmada.v1` (que ya lleva ambos emails); (4) consumer-driven contract testing: el pacto lo declara el consumidor, no es acoplamiento invertido.
- **Condiciones de test exigidas (Murat):** (1) contract test de `SolicitudCancelacionRegistrada.v1` con `HuespedEmail`+`AgenteEmail` presentes; (2) test del emisor (Reservas 4.1) que verifica que ambos campos se pueblan desde la reserva; (3) compatibilidad aditiva (un consumidor del esquema previo sigue deserializando).
- **Penalidad como estimación:** `PenalidadPorcentaje` es la SUGERIDA congelada (4.1); etiquetarla como estimación.

### Previous story intelligence

- 4.1 define y prueba `SolicitudCancelacionRegistrada.v1` (order key = ReservaId).
- 5.1a/5.1b dejan `INotificador` + inbox idempotente reutilizables.
- La penalidad final se comunica en 5.3 (no confundir estimación con cobro).

### Anti-patrones a evitar

- Presentar la penalidad estimada como cobro final (ambigüedad; el AC exige etiqueta explícita).
- Romper el contrato de `SolicitudCancelacionRegistrada.v1` (solo cambios aditivos/versionados si se enriquece).
- Duplicar el efecto (reusar el inbox idempotente de 5.1b).

### References

- [epics.md — Story 5.2 (AC-E5.2.1)](../planning-artifacts/epics.md)
- [Story 4.1 — SolicitudCancelacionRegistrada.v1](4-1-solicitar-cancelacion-con-politica-sugerida.md)
- [Story 5.1b — inbox idempotente](5-1b-worker-idempotente-sin-perdida-ni-duplicado.md)

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List

### Change Log
