---
baseline_commit: c706c941ef4221da7a8ad41b5cf66582ccae5b7d
---

# Story 4.3: Atajo de un paso, ciclo de vida y visibilidad

Status: done

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

- [x] **Task 1 — Atajo de un paso (AC: 1)** *(TDD + BDD)*
  - [x] `CancelarEnUnPasoCommand` + handler que compone `SolicitarCancelacion` (4.1) + `Resolver` (4.2) en UNA transacción, emitiendo **ambos** eventos por el outbox multi-evento (Task 0) → auditoría completa. No duplica lógica de transición.
- [x] **Task 2 — Guards del ciclo de vida (AC: 2)** *(tests de tabla)*
  - [x] `CicloDeVidaCancelacionTests`: matriz de transiciones (`Confirmada → CancelacionSolicitada → {Cancelada | Confirmada}`); prohibidas (`Cancelada → *`, `Confirmada → Cancelada` directo, doble solicitud) → guard. Cierra AC-E4.3.2.
- [x] **Task 3 — Visibilidad de pendientes con antigüedad (AC: 3)** *(TDD)*
  - [x] `ListarCancelacionesPendientesQuery` → ítems con "días en espera" = `hoy - fechaSolicitud` (reloj inyectable). Aislado por agente (`IContextoAgente`). Sin expiración automática. Puerto `ILectorCancelacionesPendientes` + SQL.
- [x] **Task 4 — Endpoints (AC: 1, 3)**
  - [x] Atajo: `POST /api/v1/reservas/{id}/cancelaciones/atajo`. Visibilidad: `GET /api/v1/reservas/cancelaciones-pendientes`. Result→HTTP.
- [x] **Task 5 — Tests (unit + integración Testcontainers)**
  - [x] BDD del atajo (ambos eventos + estado final + slot). Matriz de guards. Antigüedad correcta (reloj fijo). Aislamiento por agente. + gemelos de clasificación 2627 (overbooking/outbox) y outbox multi-evento en BD real.
- [x] **Task 6 — Commits TDD (Red→Green) + BDD en rama `feature/4-3-atajo-cancelacion` + PR a `develop`** (autor Santiago Renteria; sin trailers)

### Review Findings

<!-- Code review adversarial (Blind + Edge + Acceptance), 2026-07-09. Veredicto Auditor: cumplimiento completo
de AC-E4.3.1/.2/.3 + Task 0. -->

- [x] [Review][Patch] La clasificación `ex.Entries.Any(is NocheHabitacion)` podía tomar una colisión REAL de `UNIQUE(OutboxMessages.MessageId)` en el batch mixto del atajo como 409 [EjecutorTransaccional.cs] — ✅ refinado a `is NocheHabitacion && State == Added` (solo el INSERT de noche es overbooking) + test de batch mixto (noche Deleted + outbox Added → 500).
- [x] [Review][Patch] `DiasEnEspera` podía ser negativo con `FechaSolicitud` futura [ListarCancelacionesPendientesQueryHandler.cs] — ✅ `Math.Max(0, …)`.
- [x] [Review][Defer] `MD5.HashData` podría lanzar bajo política FIPS [ColaOutbox.cs] — deferido; entorno estándar (no FIPS); es un id de dedup interno. Si se exige FIPS, cambiar a un hash no-criptográfico determinista.
- [x] [Review][Defer] Reserva con `AgenteEmail` null irresoluble por el atajo (403 permanente) [CancelarEnUnPasoCommandHandler.cs] — deferido; mismo fail-closed ya registrado en 4.2/3.3 (backfill de `AgenteEmail`).
- [x] [Review][Defer] Estabilidad de MessageId ante 1205 solo probada a nivel unitario (falta E2E con 1205 forzado) [tests] — deferido; construir un `SqlException` 1205 real es el harness pendiente de 1.5/1.6; el diseño (semilla fija + reset por intento + dedup por UNIQUE) está cubierto por el unit de determinismo.

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

claude-opus-4-8 (Amelia, dev-story) — 2026-07-09.

### Debug Log References

- Suite completa verde: 349 tests, 0 fallos (Reservas.UnitTests 147, Reservas.IntegrationTests 47, Contracts 21, Comun.Web 15, Hoteles 100+19).
- TDD Red→Green visible en commits (`test(4.3): RED …` → `feat(4.3): GREEN …`) para el outbox multi-evento, la clasificación por entidad, el atajo y la visibilidad; BDD en dominio e integración.

### Completion Notes List

- **Task 0 (party-mode, Opción A-completa) implementada:** outbox multi-evento por comando con `MessageId` POR-MENSAJE derivado (`ColaOutbox`: ordinal 0 = MessageId del comando → cero cambio para mono-evento; ordinal>0 = MD5(semilla+ordinal), estable ante retry 1205). Desacople identidad↔clasificación por ENTIDAD ofensora en `EjecutorTransaccional` (`DbUpdateException.Entries`): `NocheHabitacion`→409, otra violación de único (MessageId outbox)→500. **Cierra el diferido de 1.6b.**
- **AC-E4.3.1 (atajo):** `CancelarEnUnPasoCommand` compone `SolicitarCancelacion` + `Resolver` sobre el mismo agregado trackeado → un `SaveChanges` → DOS eventos (`SolicitudCancelacionRegistrada` + `ReservaCancelada`/`SolicitudCancelacionRechazada`), verificado en BD real (2 filas, MessageId distinto, sin violar UNIQUE).
- **AC-E4.3.2 (guards):** matriz del ciclo de vida cubierta; transiciones fuera de `Confirmada→CancelacionSolicitada→{Cancelada|Confirmada}` → `TransicionEstadoInvalidaException`.
- **AC-E4.3.3 (visibilidad):** `ListarCancelacionesPendientesQuery` con "días en espera" (reloj inyectable), aislada por agente, sin expiración automática.
- No duplica lógica de transición; reutiliza el reloj inyectable y el patrón de query/aislamiento de 3.3.

### File List

**Comun:** (sin cambios de eventos; reutiliza los de 4.1/4.2)

**Infraestructura (Reservas.Infrastructure):**
- `Mensajeria/ColaOutbox.cs` (M — MessageId por-mensaje derivado; quita el guard "1 evento")
- `Persistencia/EjecutorTransaccional.cs` (M — clasificación del 2627 por entidad)
- `Proyeccion/LectorCancelacionesPendientesSql.cs` (A)
- `RegistroInfraestructura.cs` (M — registra el lector)

**Aplicación (Reservas.Application):**
- `Reservas/CancelarEnUnPaso/{CancelarEnUnPasoCommand,CancelarEnUnPasoCommandValidator,CancelarEnUnPasoCommandHandler}.cs` (A)
- `Reservas/ListarCancelacionesPendientes/{ListarCancelacionesPendientesQuery,ListarCancelacionesPendientesQueryHandler}.cs` (A)
- `Abstracciones/ILectorCancelacionesPendientes.cs` (A)

**Api:** `Reservas.Api/Program.cs` (M — endpoints atajo + visibilidad)

**Tests:**
- `Reservas.UnitTests/Outbox/ColaOutboxGuardTests.cs` (M — multi-evento)
- `Reservas.UnitTests/Dominio/CicloDeVidaCancelacionTests.cs` (A)
- `Reservas.UnitTests/Reservas/CancelarEnUnPaso/CancelarEnUnPasoCommandHandlerTests.cs` (A)
- `Reservas.UnitTests/Reservas/ListarCancelacionesPendientes/ListarCancelacionesPendientesQueryHandlerTests.cs` (A)
- `Reservas.IntegrationTests/OutboxClasificacionConflictoTests.cs` (A)
- `Reservas.IntegrationTests/AtajoYVisibilidadCancelacionTests.cs` (A)

### Change Log

- 2026-07-09 — Story 4.3 implementada (outbox multi-evento + atajo + visibilidad). Cierra la Épica 4. Estado → review.
