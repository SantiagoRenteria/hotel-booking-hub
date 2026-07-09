---
baseline_commit: 763121ca100292ad4ac730f4305553593df64c3e
---

# Story 5.3: Notificar la resolución de la cancelación

Status: review

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

- [x] **Task 1 — Handler de `ReservaCancelada.v1` (AC: 1)** *(TDD)*
  - [x] `ConsumidorResolucionCancelacion` deserializa `ReservaCanceladaV1` y envía al viajero la penalidad **FINAL aplicada**; si aplicada = 0% → aviso de **condonación**; si `PenalidadFueOverride` (no cero) → nota de ajuste del agente respecto a la estimación.
- [x] **Task 2 — Handler de `SolicitudCancelacionRechazada.v1` (AC: 2)** *(TDD)*
  - [x] Deserializa `SolicitudCancelacionRechazadaV1` y envía al viajero: la reserva **sigue CONFIRMADA** + el **motivo del rechazo**. Inequívoco (no habla de penalidad → no confundible con cancelación).
- [x] **Task 3 — Idempotencia + reutilización (AC: 1, 2)**
  - [x] Ambos desenlaces pasan por `IInboxIdempotencia` (5.1b) y `INotificador` (5.1a) vía `EnvioIdempotenteCorreos` — dedup por `(MessageId, version, "viajero")`. Invariante **rechazo ≠ cancelación** garantizado despachando por `evento.Type` (tests lo aseveran).
- [x] **Task 4 — Tests (unit + integración)**
  - [x] Unit (`ConsumidorResolucionCancelacionTests`): aprobación aplicando → final (no estimación); condonación (0% + override) → "condonada"; override no-cero → nota de ajuste; rechazo → "sigue Confirmada" + motivo sin hablar de penalidad; idempotencia (N→1); otro tipo ignorado; sin email no envía. Enriquecimiento del evento: contract test + emitter tests (4.2 aprobar/rechazar). Idempotencia con Redis real cubierta por `EnvioIdempotenteCorreos` (5.1b). *(Transporte diferido en todo el sistema.)*
- [x] **Task 5 — Commits TDD (Red→Green) en rama `feature/5-3-notificar-resolucion` + PR a `develop`** (autor Santiago Renteria; sin trailers). Cierra la Épica 5. — 2 ciclos Red→Green; PR pendiente al cierre.

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

claude-opus-4-8 (dev-story autónomo, Épica 5).

### Debug Log References

- `dotnet format` exigía alinear el comentario multilínea entre parámetros del record `ReservaCanceladaV1` a la columna de los trailing comments (feo). Se movió la nota al `<summary>` y se dejó un trailing comment corto en el parámetro. `SolicitudCancelacionRechazadaV1` no lo requirió (su parámetro previo no tiene trailing comment).
- Hoteles.IntegrationTests falló 19/19 en ~31ms en una corrida de la suite completa (fixture SQL no arrancó) → flake por contención de contenedores; verde en la reejecución.

### Completion Notes List

- **AC-E5.3.1/.2 cumplidos:** el viajero recibe el desenlace FINAL — penalidad aplicada / condonación (`ReservaCancelada.v1`) o rechazo con "sigue Confirmada" + motivo (`SolicitudCancelacionRechazada.v1`). Invariante rechazo ≠ cancelación garantizado por despacho por tipo.
- **Decisión de party-mode (opción a) aplicada a 5.3:** `ReservaCancelada.v1` y `SolicitudCancelacionRechazada.v1` enriquecidos ADITIVAMENTE con `HuespedEmail` (solo el viajero se notifica en la resolución → no se añadió `AgenteEmail`, YAGNI). Poblados por los emisores 4.2 (`ResolverCancelacion`) y 4.3 (`CancelarEnUnPaso`), ambas ramas.
- **Reutilización:** `ConsumidorResolucionCancelacion` usa el helper `EnvioIdempotenteCorreos` (5.2) → dedup idempotente + liberación de compensación heredadas.
- **Cierra la Épica 5.** Regresión 388 tests verdes; `dotnet format` limpio.

### File List

**Nuevos (src):**
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/ConsumidorResolucionCancelacion.cs`

**Modificados (src):**
- `src/Comun/HotelBookingHub.Comun/Eventos/ReservaCanceladaV1.cs` (+`HuespedEmail` aditivo)
- `src/Comun/HotelBookingHub.Comun/Eventos/SolicitudCancelacionRechazadaV1.cs` (+`HuespedEmail` aditivo)
- `src/Servicios/Reservas/Reservas.Application/Reservas/ResolverCancelacion/ResolverCancelacionCommandHandler.cs` (puebla email, ambas ramas)
- `src/Servicios/Reservas/Reservas.Application/Reservas/CancelarEnUnPaso/CancelarEnUnPasoCommandHandler.cs` (puebla email, ambas ramas)
- `src/Servicios/Notificaciones/Notificaciones.Worker/Program.cs` (registra el consumidor)

**Nuevos (tests):**
- `tests/Notificaciones.UnitTests/ConsumidorResolucionCancelacionTests.cs`

**Modificados (tests):**
- `tests/Contracts/ContratoEventosResolucionCancelacionTests.cs` (email en el contrato)
- `tests/Reservas.UnitTests/Reservas/ResolverCancelacion/ResolverCancelacionCommandHandlerTests.cs` (emitter tests aprobar/rechazar)

### Change Log

- 2026-07-09 — Ciclo A: enriquecimiento aditivo de `ReservaCancelada.v1`/`SolicitudCancelacionRechazada.v1` con `HuespedEmail` + emisores 4.2/4.3. Red→Green.
- 2026-07-09 — Ciclo B: `ConsumidorResolucionCancelacion` (penalidad final / condonación / rechazo) sobre `EnvioIdempotenteCorreos`. Red→Green.
- 2026-07-09 — Regresión completa (388 tests) verde + `dotnet format` limpio; Status → review.
