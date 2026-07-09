---
baseline_commit: c706c941ef4221da7a8ad41b5cf66582ccae5b7d
---

# Story 4.3: Atajo de un paso, ciclo de vida y visibilidad

Status: in-progress

<!-- Generado por bmad-create-story. Complejidad NORMAL-ALTA (compone 4.1+4.2; añade query de visibilidad).
BDD (Given/When/Then) + TDD Red→Green. Sin Task 0 propio: reutiliza el modelo de dominio de 4.1/4.2. -->

## Story

Como **agente**,
quiero **solicitar y resolver una cancelación en una sola operación y ver la antigüedad de las pendientes**,
para **atender al viajero por teléfono sin perder trazabilidad**.

Cierra el ciclo de vida (atajo de un paso auditado) y da visibilidad operativa (antigüedad de pendientes, sin
expiración automática).

## Acceptance Criteria

1. **AC-E4.3.1 — Atajo auditado.** **Dado** una reserva `Confirmada`, **cuando** el agente ejecuta el atajo
   (solicitar + resolver), **entonces** se registran **ambos** eventos (solicitud y resolución) para auditoría.
2. **AC-E4.3.2 — Guards del ciclo de vida.** **Dado** cualquier transición, **cuando** se intenta salir del ciclo
   `Confirmada → CancelacionSolicitada → {Cancelada | Confirmada}`, **entonces** una transición no permitida se
   rechaza por guard.
3. **AC-E4.3.3 — Antigüedad visible.** **Dado** solicitudes pendientes, **cuando** se listan, **entonces** cada
   una expone sus "días en espera"; no hay expiración automática.

## Tasks / Subtasks

> **✅ Task 0 RESUELTA (party-mode Winston+Murat+Amelia, 2026-07-09) — Outbox multi-evento por comando (Opción A-completa).**
> El atajo emite DOS eventos en un comando, pero el outbox imponía "un evento por comando". Decisión:
> - **MessageId por-MENSAJE derivado determinista** (cierra el diferido de 1.6b): en `ColaOutbox`, `ordinal = RegistrarEventoEncolado()-1`; `messageId = ordinal==0 ? contexto.MessageId : Guid.CreateVersion5(contexto.MessageId, BitConverter.GetBytes(ordinal))`. **Ordinal 0 = MessageId del comando** → CERO cambio observable para los comandos mono-evento (1.3/1.6b/2.5/4.1/4.2). Estable ante retry 1205 porque la semilla se fija una vez en `TransactionBehavior` y `ReiniciarConteo()` corre por-intento → misma secuencia de ordinales → mismos ids → la `UNIQUE(MessageId)` deduplica. Quitar el `throw` del guard "1 evento/comando".
> - **Desacople identidad↔clasificación (por ENTIDAD, sin parsear mensaje):** en el catch 2627/2601 de `EjecutorTransaccional`, clasificar por la entidad ofensora vía `DbUpdateException.Entries`: si hay `NocheHabitacion` → `HabitacionNoDisponibleException` (409, overbooking); en cualquier otra violación de único (p. ej. `UNIQUE(OutboxMessages.MessageId)`) → **rethrow como NO-de-negocio → 500**, nunca 409. `ClasificacionSqlServer` (por `Number`) intacto.
> - **NO tocar:** envelope `EventoIntegracion` (1.3), `TransactionBehavior` (semilla-una-vez + reset-por-intento), retry 1205, outbox de Hoteles.
> - **Handler del atajo:** compone `SolicitarCancelacion` (4.1) + `Resolver` (4.2) sobre el MISMO agregado trackeado (`ObtenerConNochesAsync`) → un `SaveChanges` → dos `Encolar`. Encolar SolicitudCancelacionRegistrada ANTES que la resolución; ambos con `AggregateId = ReservaId`.
> - **Tests obligatorios (Murat):** atajo-aprobado → exactamente {Registrada, ReservaCancelada} (2 filas); atajo-rechazo → {Registrada, Rechazada} y `Count(ReservaCancelada)==0`; estabilidad de MessageId ante 1205 forzado (2 filas, no 4); **colisión de MessageId → 500 (no 409) + test gemelo overbooking → 409**; regresión mono-evento (`CrearReserva` = 1 fila, id == comando); money-test G1 sigue verde; guard relajado a N pero fail-fast.

- [ ] **Task 1 — Atajo de un paso (AC: 1)** *(TDD + BDD)*
  - [ ] `CancelarEnUnPasoCommand` + handler que compone solicitar + resolver en UNA transacción, emitiendo **ambos** eventos (solicitud y resolución) por outbox → auditoría completa. Reutiliza las transiciones de dominio de 4.1/4.2 (no duplicar lógica).
- [ ] **Task 2 — Guards del ciclo de vida (AC: 2)** *(tests de tabla)*
  - [ ] Test exhaustivo de la matriz de transiciones del agregado (`Confirmada → CancelacionSolicitada → {Cancelada | Confirmada}`); toda transición no permitida (p. ej. `Cancelada → *`, `Confirmada → Cancelada` directo fuera del atajo) → rechazada por guard. Cierra AC-E4.3.2 a nivel de dominio.
- [ ] **Task 3 — Visibilidad de pendientes con antigüedad (AC: 3)** *(TDD)*
  - [ ] `ListarCancelacionesPendientesQuery` (o extender el listado de 3.3) → ítems con "días en espera" = `hoy - fechaSolicitud` (reloj inyectable). Aislado por agente (`IContextoAgente`, patrón 3.3). Sin expiración automática (solo se expone la antigüedad).
- [ ] **Task 4 — Endpoints (AC: 1, 3)**
  - [ ] Atajo: `POST /api/v1/reservas/{id}/cancelaciones/atajo` (solicitar+resolver). Visibilidad: `GET /api/v1/reservas/cancelaciones-pendientes`. Result→HTTP.
- [ ] **Task 5 — Tests (unit + integración Testcontainers)**
  - [ ] BDD del atajo (Given confirmada → When atajo → Then ambos eventos + estado final). Matriz de guards. Antigüedad correcta (reloj fijo). Aislamiento por agente.
- [ ] **Task 6 — Commits TDD (Red→Green) + BDD en rama `feature/4-3-atajo-cancelacion` + PR a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Arquitectura y archivos a tocar

- **Dominio:** reutilizar `SolicitarCancelacion` + `Resolver` de 4.1/4.2; el atajo es una composición, NO nueva lógica de transición. La matriz de guards (AC-E4.3.2) valida el invariante del ciclo de vida ya construido.
- **Visibilidad:** query de lectura (patrón 3.3), aislada por `AgenteEmail` vía `IContextoAgente`. "Días en espera" con reloj inyectable (`TimeProvider`/`IClock`) para tests deterministas — mismo reloj que la congelación de 4.1.
- **Auditoría del atajo:** ambos eventos (`SolicitudCancelacionRegistrada` + `ReservaCancelada`/`SolicitudCancelacionRechazada`) en la misma tx → sin decisiones huérfanas.

### Previous story intelligence

- 4.1 y 4.2 dejan las transiciones de dominio y los eventos; 4.3 compone y expone. Si 4.1/4.2 dejaron el reloj inyectable, reutilizarlo aquí (no re-introducir `DateTime.UtcNow`).
- Patrón de query/aislamiento de 3.3 directamente aplicable a la visibilidad de pendientes.

### Anti-patrones a evitar

- Duplicar la lógica de transición en el atajo (debe componer 4.1/4.2).
- Expiración automática de pendientes (el AC dice explícitamente que NO la hay; solo exponer antigüedad).
- Antigüedad con reloj no inyectable (test no determinista).
- Listar pendientes de otros agentes (aislamiento, 403/filtrado como 3.3).

### References

- [epics.md — Story 4.3 (AC-E4.3.x)](../planning-artifacts/epics.md)
- [Story 4.1](4-1-solicitar-cancelacion-con-politica-sugerida.md) / [Story 4.2](4-2-resolver-cancelacion-con-auditoria.md)
- [Story 3.3](3-3-listado-de-reservas-del-agente-con-detalle.md) (query de lectura aislada por agente)

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List

### Change Log
