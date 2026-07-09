# Story 4.1: Solicitar cancelación con política sugerida

Status: ready-for-dev

<!-- Generado por bmad-create-story. Complejidad ALTA (núcleo de dominio: máquina de estados + penalidad
congelada + evento nuevo + concurrencia). 2ª VITRINA BDD: aplicar BDD (Given/When/Then) además de TDD Red→Green
visible en commits. Tiene Task 0 party-mode (modelo de dominio de cancelación, compartido por toda la Épica 4). -->

## Story

Como **viajero (o agente en su nombre)**,
quiero **solicitar la cancelación de una reserva confirmada indicando el motivo**,
para **iniciar el proceso y conocer la penalidad estimada**.

Primer eslabón del ciclo de vida de cancelación (E4, diferenciador). La penalidad es **sugerencia congelada** en
la fecha de solicitud, no imposición. E4 **define y prueba sus eventos** aunque E5 (notificaciones) aún no los
consuma (regla de propiedad de eventos, party-mode Winston).

## Acceptance Criteria

1. **AC-E4.1.1 — Solicitud válida y penalidad sugerida congelada.**
   **Dado** una reserva `Confirmada` con estancia no iniciada,
   **cuando** se solicita la cancelación con motivo (categoría + texto libre) e `Iniciador` (viajero/agente),
   **entonces** la reserva pasa a `CancelacionSolicitada`, se **congela** la `PenalidadSugerida` (ref = fecha de
   solicitud: `>=30` días a la entrada → `0%`; `<30` días → `100%`) y se escribe `SolicitudCancelacionRegistrada`
   en el outbox; **y** la respuesta **incluye** la penalidad como valor informativo (no se cobra).
2. **AC-E4.1.2 — Solicitud duplicada (AC negativo).** Dado una reserva con una solicitud de cancelación en curso,
   cuando se solicita otra, **entonces** `409`.
3. **AC-E4.1.3 — Estado no elegible (AC negativo).** Dado una reserva que no está `Confirmada` (o con estancia ya
   iniciada), cuando se solicita la cancelación, **entonces** `409`/`422` según el guard, sin cambiar de estado.

## Tasks / Subtasks

> **Task 0 (party-mode, PRIMERO) — modelo de dominio de cancelación (compartido por toda la Épica 4).**
> Decisiones a cerrar con `/bmad-party-mode` (Winston + Amelia + Murat) antes de implementar:
> - **Máquina de estados** en `EstadoReserva` (`Confirmada → CancelacionSolicitada → {Cancelada | Confirmada}`):
>   ¿guards en el agregado `Reserva` (métodos `SolicitarCancelacion`/`Resolver`) que lanzan/retornan según estado?
> - **Modelo de penalidad:** ¿VO `PoliticaCancelacion`/`PenalidadSugerida` (porcentaje + referencia congelada)?
>   ¿domain service puro (como `CalculadorPrecio`) o regla en el agregado? Regla actual: `>=30`d→0%, `<30`d→100%.
> - **Congelación:** persistir la penalidad + fecha de solicitud + motivo (categoría+texto) + `Iniciador` en la
>   `Reserva` (¿owned `SolicitudCancelacion`?). Migración.
> - **Concurrencia:** `rowVersion` ya existe en `Reserva`; la solicitud duplicada (AC-E4.1.2) se arbitra por
>   guard de estado + optimistic concurrency (→ 409). Confirmar mapeo 409 vs 422 para estado no elegible.
> - **Evento** `SolicitudCancelacionRegistrada.v1` en `Comun.Eventos` (contrato + contract test) — E4 lo define.
> Documentar la decisión (como en 3.1/3.3) antes de codificar.

- [ ] **Task 1 — Máquina de estados + VOs de dominio (AC: 1, 3)** *(TDD + BDD)*
  - [ ] `EstadoReserva` gana `CancelacionSolicitada`, `Cancelada`. `Reserva.SolicitarCancelacion(motivo, iniciador, fechaSolicitud, politica)` con guard (solo desde `Confirmada` y estancia no iniciada); tests de tabla de transiciones (permitidas/rechazadas).
  - [ ] VO(s) de penalidad/solicitud (motivo categoría+texto, `Iniciador`, `PenalidadSugerida` congelada, fecha).
- [ ] **Task 2 — Cálculo de la penalidad sugerida (AC: 1)** *(TDD, tests de borde)*
  - [ ] Regla `>=30`d→0% / `<30`d→100% con **bordes** (exactamente 30 días, día de la entrada). Domain service puro o regla del VO (según Task 0).
- [ ] **Task 3 — Command + handler + evento (AC: 1, 2, 3)** *(TDD Red→Green)*
  - [ ] `SolicitarCancelacionCommand` + handler (`ICommand` → pasa por `TransactionBehavior`: agregado + outbox en una tx). Encola `SolicitudCancelacionRegistrada.v1`. Validación de entrada (motivo obligatorio) en validator.
  - [ ] Guards → `Result`: no elegible → 409/422; duplicada → 409.
- [ ] **Task 4 — Contrato del evento (BDD/contract test)**
  - [ ] `SolicitudCancelacionRegistrada.v1` en `Comun.Eventos` + pin en `ContratoEventosCatalogoTests` (o test de contrato propio de eventos de reserva).
- [ ] **Task 5 — Endpoint (AC: 1, 2, 3)**
  - [ ] `POST /api/v1/reservas/{id}/cancelaciones` (o `.../solicitud-cancelacion`) en `Reservas.Api`; Result→HTTP; respuesta incluye la penalidad estimada. OpenAPI.
- [ ] **Task 6 — Tests (unit + integración Testcontainers)**
  - [ ] BDD del happy path (Given confirmada → When solicita → Then CancelacionSolicitada + penalidad congelada + evento en outbox); AC negativos (duplicada 409, no elegible 409/422); congelación persistida (round-trip).
- [ ] **Task 7 — Commits TDD (Red→Green visibles) + BDD en rama `feature/4-1-solicitar-cancelacion` + PR a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Arquitectura y archivos a tocar

- **Dominio (Reservas.Domain/Reservas):** `EstadoReserva` (nuevos estados), `Reserva` (transición `SolicitarCancelacion` con guard + persistir la solicitud/penalidad), nuevos VO(s) de penalidad/motivo/iniciador. Posible domain service de penalidad (patrón `CalculadorPrecio`).
- **Escritura transaccional:** reutilizar `EjecutorTransaccional` + `IColaOutbox` + `TransactionBehavior` (comando → agregado + outbox en UNA tx), igual que `CrearReserva` (1.6b). [Source: CrearReservaCommandHandler.cs]
- **Persistencia:** mapeo EF de la solicitud de cancelación (owned) en `ReservasDbContext` + migración. `rowVersion` ya está en `Reserva` (concurrencia optimista).
- **Evento:** `Comun.Eventos/SolicitudCancelacionRegistrada.v1` (payload: AggregateId=ReservaId, penalidad, iniciador, motivo…; order key por Version si aplica). E4 define y prueba el contrato (E5 lo consumirá).
- **Api:** endpoint en `Reservas.Api/Program.cs`; `Result→ToOkResult`/`ToCreatedResult`.

### Previous story intelligence (E3)

- `Reserva` ya persiste `AgenteEmail` (3.3): habilita registrar quién inicia (auditoría de E4). El listado de 3.3 mostrará el nuevo estado; no romperlo.
- Patrón de query de lectura y `IContextoAgente` (3.3) disponibles para 4.3 (visibilidad de pendientes) — no para 4.1.
- Estancia semiabierta `[entrada, salida)`; "estancia no iniciada" = `hoy < entrada` (definir con reloj inyectable para tests deterministas). Ojo: `Date.now`/reloj — usar un `IClock`/`TimeProvider` inyectable, no `DateTime.UtcNow` directo, para testear la congelación por fecha.

### Anti-patrones a evitar

- Cambiar el estado sin guard (permite transiciones ilegales — AC-E4.3.2).
- Recalcular la penalidad al resolver (debe estar **congelada** desde la solicitud).
- Usar `DateTime.UtcNow` directo en la regla de penalidad (no testeable de forma determinista).
- Escribir el evento fuera de la transacción del agregado (pérdida/duplicado).

### References

- [epics.md — Story 4.1 (AC-E4.1.x), regla de propiedad de eventos](../planning-artifacts/epics.md)
- [Story 3.3](3-3-listado-de-reservas-del-agente-con-detalle.md) (persistencia de Reserva, IContextoAgente)
- [CrearReserva (1.6b)](1-6b-atomicidad-transaccional-reserva-outbox.md) (patrón outbox transaccional)

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List

### Change Log
