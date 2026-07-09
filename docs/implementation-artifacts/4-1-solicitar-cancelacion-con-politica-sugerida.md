---
baseline_commit: 023e2bb3d96d99a0e3a4b84e0772b90959bcd731
---

# Story 4.1: Solicitar cancelación con política sugerida

Status: done

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

> **✅ Task 0 RESUELTA (party-mode Winston + Murat, 2026-07-09; Amelia N/A por glitch de entorno).** Modelo de
> dominio de cancelación (compartido por toda la Épica 4):
> - **Guards por EXCEPCIÓN de dominio, no `Result`** (coherencia con `EstanciaInvalidaException`): `Reserva.SolicitarCancelacion/Resolver` lanzan `TransicionEstadoInvalidaException` (una sola, con estado origen/destino) → Api mapea a **409**. Guard DENTRO del método del agregado. "Estancia ya iniciada" (`hoy >= entrada`) = **no elegible → 409** (no penalidad 100%).
> - **Penalidad: domain service PURO** `CalculadorPenalidad(Estancia, DateOnly fechaSolicitud) → PenalidadSugerida` (VO inmutable), familia de `CalculadorPrecio`. El **reloj (`TimeProvider`) se resuelve en el HANDLER** y se pasa como `DateOnly` — el dominio no toca el reloj (determinista). Regla `>=30`d→0% / `<30`d→100% con constante+`if` (NO tabla configurable). **CONGELADA en la solicitud; 4.2 NUNCA la recalcula.**
> - **Persistencia: owned `SolicitudCancelacion` NULLABLE** en `Reserva` (motivo categoría+texto, `IniciadaPor`, penalidad congelada, `FechaSolicitud`); al resolver, la MISMA owned recibe `ResueltaPor`+`FechaResolucion`+resultado (un solo episodio, no dos owned). Migración. `EstadoReserva` como string (ya se mapea así).
> - **Evento** `SolicitudCancelacionRegistrada.v1` en `Comun.Eventos`, **order key = `ReservaId`**, con **contract test propio de eventos de reserva** (separado del de catálogo). Por el outbox existente.
> - **Concurrencia/doble liberación (4.2, invariante duro):** el borrado de `NochesHabitacion` va en la MISMA tx que la transición + `rowVersion++`. La 2ª resolución concurrente choca con `DbUpdateConcurrencyException` → aborta la tx (su borrado se revierte) → **409**. Candado optimista + guard de estado; SIN locks pesimistas ni contadores. Regla no negociable: borrado de slots comparte la tx del bump de versión.
> - **Tests obligatorios (Murat):** bordes de penalidad (30d→0%, 29d→100%, día de entrada, estancia iniciada→no elegible); **congelación** (avanzar reloj entre solicitud y aprobación, % no cambia); round-trip de liberación con paso intermedio 409 (2→3→201); doble resolución concurrente (patrón G1, `Barrier`, 1×2xx/1×409, repetido N); tabla de transiciones prohibidas (`Cancelada→*`, salto directo, doble solicitud sin recongelar); outbox (exactamente-un `ReservaCancelada`, **rechazar NO emite `ReservaCancelada`**, atomicidad estado+evento).

- [x] **Task 1 — Máquina de estados + VOs de dominio (AC: 1, 3)** *(TDD + BDD)*
  - [x] `EstadoReserva` gana `CancelacionSolicitada`, `Cancelada`. `Reserva.SolicitarCancelacion(motivo, iniciador, fechaSolicitud, politica)` con guard (solo desde `Confirmada` y estancia no iniciada); tests de tabla de transiciones (permitidas/rechazadas).
  - [x] VO(s) de penalidad/solicitud (motivo categoría+texto, `Iniciador`, `PenalidadSugerida` congelada, fecha).
- [x] **Task 2 — Cálculo de la penalidad sugerida (AC: 1)** *(TDD, tests de borde)*
  - [x] Regla `>=30`d→0% / `<30`d→100% con **bordes** (exactamente 30 días, día de la entrada). Domain service puro o regla del VO (según Task 0).
- [x] **Task 3 — Command + handler + evento (AC: 1, 2, 3)** *(TDD Red→Green)*
  - [x] `SolicitarCancelacionCommand` + handler (`ICommand` → pasa por `TransactionBehavior`: agregado + outbox en una tx). Encola `SolicitudCancelacionRegistrada.v1`. Validación de entrada (motivo obligatorio) en validator.
  - [x] Guards por **excepción de dominio** (Task 0, supera la nota original de `Result`): no elegible / duplicada → `TransicionEstadoInvalidaException` → 409.
- [x] **Task 4 — Contrato del evento (BDD/contract test)**
  - [x] `SolicitudCancelacionRegistrada.v1` en `Comun.Eventos` + `ContratoSolicitudCancelacionRegistradaTests` (test de contrato propio de eventos de reserva).
- [x] **Task 5 — Endpoint (AC: 1, 2, 3)**
  - [x] `POST /api/v1/reservas/{id}/solicitud-cancelacion` en `Reservas.Api`; Result→HTTP (200 con penalidad; guards→409). OpenAPI vía `.WithName`/`.WithTags`.
- [x] **Task 6 — Tests (unit + integración Testcontainers)**
  - [x] BDD del happy path (Given confirmada → When solicita → Then CancelacionSolicitada + penalidad congelada + evento en outbox); AC negativos (duplicada 409, estancia iniciada 409); congelación persistida (round-trip).
- [x] **Task 7 — Commits TDD (Red→Green visibles) + BDD en rama `feature/4-1-solicitar-cancelacion` + PR a `develop`** (autor Santiago Renteria; sin trailers)

### Review Findings

<!-- Code review adversarial (Blind Hunter + Edge Case Hunter + Acceptance Auditor), 2026-07-09. -->

- [x] [Review][Patch] Motivo sin `MaximumLength` → truncamiento SQL = 500 en vez de 400 [SolicitarCancelacionCommandValidator.cs] — ✅ `MaximumLength(80/1000)` en el validator + tests (borde exacto válido, +1 inválido). Ahora corta en 400.
- [x] [Review][Patch] Enum `Iniciador` en el body solo bindea número (asimétrico con el evento que emite "Viajero"/"Agente") [Reservas.Api/Program.cs] — ✅ `JsonStringEnumConverter` registrado vía `ConfigureHttpJsonOptions` + test de contrato del binding por nombre.
- [x] [Review][Defer] Concurrencia optimista: 2ª solicitud en carrera → `DbUpdateConcurrencyException` no traducida → 500 en vez de 409 [EjecutorTransaccional.cs] — deferido a **Story 4.2** (Task 0 asigna la arbitración de concurrencia/doble-liberación a 4.2; requiere cambio de infra compartida + tests).
- [x] [Review][Defer] "Hoy" en UTC vs fecha calendario de la estancia (borde de zona horaria puede invertir 0%↔100% o la elegibilidad por un día) [SolicitarCancelacionCommandHandler.cs] — deferido; convención UTC+`DateOnly` de todo el sistema (pre-existente, ver `HuespedDtoValidator`).
- [x] [Review][Defer] Autorización: sin aislamiento por agente (IDOR) y `Iniciador` autodeclarado por el cliente [Reservas.Api/Program.cs, handler] — deferido a **Épica 6** (auth/RBAC/aislamiento).
- [x] [Review][Defer] Sin test a nivel HTTP del endpoint (200/400/409 + binding JSON) [SolicitudCancelacionTests.cs] — deferido; los tests usan `ISender` (patrón del repo), el 409 se infiere del tipo base `ExcepcionNegocio`.

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

claude-opus-4-8 (Amelia, dev-story) — 2026-07-09.

### Debug Log References

- Suite completa verde: 296 tests, 0 fallos (Reservas.UnitTests 108, Reservas.IntegrationTests 38, Contracts 16, Comun.Web 15, Hoteles.UnitTests 100, Hoteles.IntegrationTests 19).
- TDD Red→Green visible en commits: `test(4.1): RED …` → `feat(4.1): GREEN …` para dominio y para el handler.

### Completion Notes List

- **Task 0 (party-mode) implementada tal cual:** guards por EXCEPCIÓN de dominio (`TransicionEstadoInvalidaException` → 409), NO `Result` (esto supera la nota original de Task 1/3 que mencionaba `Result`; se documentó en los checkboxes).
- **Penalidad congelada:** `CalculadorPenalidad` (domain service puro) `>=30`d→0% / `<30`d→100%; el reloj se resuelve en el handler (`TimeProvider`) y se pasa como `DateOnly` (determinismo). La penalidad se congela dentro del agregado y se persiste; el monto es informativo (`PenalidadSugerida.MontoSobre`).
- **Estancia iniciada** (`fechaSolicitud >= entrada`) = no elegible → 409 (NO penalidad 100%), guard dentro del agregado.
- **Duplicada** cubierta por el guard de estado (2ª solicitud ya no está `Confirmada` → 409) sin re-congelar.
- **Persistencia:** owned reference NULLABLE `SolicitudCancelacion` (un solo episodio; 4.2 completará la resolución) + migración `AgregaSolicitudCancelacion`.
- **Evento:** `SolicitudCancelacionRegistrada.v1` (order key = `ReservaId`) con contract test propio de eventos de reserva; encolado en la misma tx del agregado (patrón 1.6b), verificado end-to-end en el outbox.
- **Diferido a 4.2 (fuera de alcance de 4.1):** resolución (aprobar/rechazar), borrado de `NochesHabitacion` en la misma tx + doble-liberación por `rowVersion`, estado `Cancelada`.

### File List

**Dominio (Reservas.Domain):**
- `Reservas/EstadoReserva.cs` (M — nuevos estados)
- `Reservas/Reserva.cs` (M — owned `SolicitudCancelacion` + método `SolicitarCancelacion`)
- `Reservas/TransicionEstadoInvalidaException.cs` (A)
- `Reservas/IniciadorCancelacion.cs` (A)
- `Reservas/MotivoCancelacion.cs` (A)
- `Reservas/PenalidadSugerida.cs` (A)
- `Reservas/SolicitudCancelacion.cs` (A)
- `Servicios/CalculadorPenalidad.cs` (A)
- `Puertos/IReservaRepository.cs` (M — `ObtenerAsync`)

**Aplicación (Reservas.Application):**
- `Reservas/SolicitarCancelacion/SolicitarCancelacionCommand.cs` (A — command + response DTO + request HTTP)
- `Reservas/SolicitarCancelacion/SolicitarCancelacionCommandHandler.cs` (A)
- `Reservas/SolicitarCancelacion/SolicitarCancelacionCommandValidator.cs` (A)

**Comun:**
- `Eventos/SolicitudCancelacionRegistradaV1.cs` (A)

**Infraestructura (Reservas.Infrastructure):**
- `Persistencia/ReservasDbContext.cs` (M — owned mapping)
- `Persistencia/ReservaRepository.cs` (M — `ObtenerAsync`)
- `Migraciones/20260709182050_AgregaSolicitudCancelacion.cs` (+ Designer + snapshot) (A/M)

**Api:**
- `Reservas.Api/Program.cs` (M — DI + endpoint)

**Tests:**
- `Reservas.UnitTests/Dominio/CalculadorPenalidadTests.cs` (A)
- `Reservas.UnitTests/Dominio/ReservaCancelacionTests.cs` (A)
- `Reservas.UnitTests/Reservas/SolicitarCancelacion/{RelojFijo,SolicitarCancelacionCommandHandlerTests,SolicitarCancelacionCommandValidatorTests}.cs` (A)
- `Reservas.UnitTests/Reservas/CrearReserva/Fakes.cs` (M — `ObtenerAsync`)
- `Contracts/ContratoSolicitudCancelacionRegistradaTests.cs` (A)
- `Reservas.IntegrationTests/SolicitudCancelacionTests.cs` (A)

### Change Log

- 2026-07-09 — Story 4.1 implementada (dominio + aplicación + infraestructura + api + tests). Estado → review.
