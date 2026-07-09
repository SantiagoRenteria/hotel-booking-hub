---
baseline_commit: b2a429d1540d9fdb20bbfa59bc33ba222e26d00d
---

# Story 5.2: Notificar la solicitud de cancelación

Status: review

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

- [x] **Task 1 — Handler del evento `SolicitudCancelacionRegistrada.v1` (AC: 1)** *(TDD)*
  - [x] `ConsumidorSolicitudCancelacion` deserializa el evento (tipo/`JsonElement`, patrón de 3.1/5.1a). Acuse al viajero con la penalidad **etiquetada como ESTIMACIÓN** (no cobro) y aviso "por resolver" al agente.
  - [x] Reutiliza `INotificador` (5.1a) y el inbox idempotente (5.1b) vía `EnvioIdempotenteCorreos` — dedup por `(MessageId, version, destinatario)`.
- [x] **Task 2 — Copy inequívoco (AC: 1)**
  - [x] El correo del viajero declara la penalidad **ESTIMADA** (congelada en la fecha de solicitud), aclarando que el importe definitivo se confirma al resolverse (no es el cobro final, que llega en 5.3). El del agente indica "por resolver". Aserciones en `ConsumidorSolicitudCancelacionTests`.
- [x] **Task 3 — Tests (unit + integración)**
  - [x] Unit (`ConsumidorSolicitudCancelacionTests`): acuse al viajero (% + etiqueta "estimación", sin "cobro final"), aviso al agente, idempotencia (N entregas → 1/destinatario), email de huésped nulo se omite, otro tipo se ignora. Enriquecimiento del evento: contract test (`ContratoSolicitudCancelacionRegistradaTests`) + test del emisor (`SolicitarCancelacionCommandHandlerTests`). La idempotencia con Redis real está cubierta por `EnvioIdempotenteCorreos` (helper compartido) vía los tests de integración de 5.1b (mismo inbox SETNX+TTL). *(Transporte productor→worker diferido en todo el sistema.)*
- [x] **Task 4 — Commits TDD (Red→Green) en rama `feature/5-2-notificar-solicitud-cancelacion` + PR a `develop`** (autor Santiago Renteria; sin trailers) — 2 ciclos Red→Green; PR pendiente al cierre.

### Review Findings (bmad-code-review 2026-07-09 · Blind + Edge + Auditor)

- [ ] [Review][Patch] P1 — `FechaSolicitud` se formatea con `CurrentCulture` (interpolación) mientras la penalidad usa `InvariantCulture`; bajo culturas no gregorianas/dígitos no-ASCII el correo varía. Usar `InvariantCulture` para la fecha (y corregir el mismo patrón preexistente en la `estancia` de `ConsumidorReservaConfirmada`). [ConsumidorSolicitudCancelacion.cs, ConsumidorReservaConfirmada.cs]
- [ ] [Review][Patch] P2 — Falta el test de la condición 3 de Murat: un payload del esquema previo (sin `huespedEmail`/`agenteEmail`) debe seguir deserializando (a null). Añadir contract test de compatibilidad aditiva. [tests/Contracts/ContratoSolicitudCancelacionRegistradaTests.cs]
- [ ] [Review][Patch] P3 — El emisor 4.3 (`CancelarEnUnPasoCommandHandler`) puebla los emails sin test (asimetría con 4.1). Añadir aserción del evento. [tests/Reservas.UnitTests/.../CancelarEnUnPasoCommandHandlerTests.cs]
- [x] [Review][Defer] D1 — Enrutamiento multi-consumidor: `ConsumidorSolicitudCancelacion` no se registra como `IProcesadorEvento` y el `Despachador` inyecta uno solo; registrar ambos ingenuamente lo rompería. Router por tipo al cablear el transporte real (hoy sin pump → no es bug en runtime). [Program.cs] — deferred
- [x] [Review][Defer] D2 — El atajo de un paso (4.3) emite `SolicitudCancelacionRegistrada` + la resolución; el consumidor 5.2 avisaría "estimada/por resolver" para algo ya resuelto → contradictorio/duplicado cuando 5.3 exista. Revisar en 5.3 (guard/supresión o copy). [CancelarEnUnPasoCommandHandler.cs, ConsumidorSolicitudCancelacion.cs] — deferred
- [x] [Review][Defer] D3 — "Huésped principal = primero de la colección" sin orden garantizado ni concepto de titular en el dominio; en reservas multi-huésped el acuse podría ir a un acompañante. Introducir "huésped titular" cuando el dominio lo requiera. [SolicitarCancelacionCommandHandler.cs, CancelarEnUnPasoCommandHandler.cs] — deferred
- [x] [Review][Defer] D4 — `EnvioIdempotenteCorreos` omite destinatario nulo/vacío en silencio (sin log): una falla de ruteo es indistinguible de éxito. Añadir observabilidad (log al omitir) al cablear transporte/dead-letter. [EnvioIdempotenteCorreos.cs] — deferred

## Dev Notes

### Arquitectura y archivos a tocar

- **Worker:** nuevo handler para `SolicitudCancelacionRegistradaV1` (`Comun.Eventos`, definido/probado en 4.1). Reutiliza `INotificador` + `IInboxIdempotencia` de 5.1a/5.1b.
- **Datos del correo:** el evento NO trae el email del viajero ni del agente directamente (lleva `Iniciador`, motivo, penalidad, fecha).
- ✅ **DECISIÓN (party-mode 5.2, unánime Winston/Amelia/John/Murat, aprobada por delegación de Santiago 2026-07-09): Opción (a)** — enriquecer `SolicitudCancelacionRegistrada.v1` (y en 5-3, `ReservaCancelada.v1`/`SolicitudCancelacionRechazada.v1`) de forma **ADITIVA** con `HuespedEmail` + `AgenteEmail`, poblados por los emisores de Reservas (4.1 `SolicitarCancelacion` y el atajo 4.3 `CancelarEnUnPaso`). Razones: (1) menor riesgo runtime (evento autocontenido, worker stateless, verificable en CI) vs la proyección local (b) que sufriría **expiración de TTL en cancelaciones tardías** —el caso común— y un **invariante inter-evento que ningún contract test cubre**; (2) E5 es el **primer y único consumidor** de estos eventos → el cambio aditivo no rompe a nadie; (3) consistencia con el precedente de `ReservaConfirmada.v1` (que ya lleva ambos emails); (4) consumer-driven contract testing: el pacto lo declara el consumidor, no es acoplamiento invertido.
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

claude-opus-4-8 (dev-story autónomo, Épica 5).

### Debug Log References

- Test `Avisa_al_viajero...`: fallo inicial por el propio copy — el cuerpo decía "no es el **cobro final**" y la aserción `DoesNotContain("cobro final")` lo detectaba. Reformulado a "el importe definitivo se confirmará cuando se resuelva".
- Fallo transitorio (1/6) de `Notificaciones.IntegrationTests` en una corrida de la suite completa; determinístico verde en aislamiento y en la reejecución → flake por contención de contenedores en paralelo. Se endureció `La_reserva_expira_por_TTL` (margen de 500ms→2.5s sobre el TTL de 1s).

### Completion Notes List

- **AC-E5.2.1 cumplido:** al consumir `SolicitudCancelacionRegistrada.v1`, el viajero recibe un acuse con la penalidad **etiquetada como ESTIMACIÓN** (no cobro final) y el agente un aviso "por resolver".
- **Decisión de party-mode (opción a) implementada:** el evento se enriqueció ADITIVAMENTE con `HuespedEmail`+`AgenteEmail`, poblados por los emisores de Reservas (4.1 `SolicitarCancelacionCommandHandler` y 4.3 `CancelarEnUnPasoCommandHandler`) desde la reserva; el consumidor es una función pura evento→correo, sin estado. Contract test + emitter test cubren las condiciones de Murat.
- **DRY:** se extrajo `EnvioIdempotenteCorreos` (patrón reservar→enviar→liberar por destinatario con agregación de fallos + liberación con `CancellationToken.None`, hallazgos F1/F2 de 5.1b) y se refactorizó `ConsumidorReservaConfirmada` para reusarlo (5.1b sigue verde).
- **Nullabilidad:** `HuespedEmail`/`AgenteEmail` son nullable (fidelidad al dominio: `Reserva.AgenteEmail` es nullable); el consumidor omite el destinatario sin dirección.
- **Regresión:** 377 tests verdes; `dotnet format` limpio.

### File List

**Nuevos (src):**
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/ConsumidorSolicitudCancelacion.cs`
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/EnvioIdempotenteCorreos.cs`

**Modificados (src):**
- `src/Comun/HotelBookingHub.Comun/Eventos/SolicitudCancelacionRegistradaV1.cs` (campos aditivos `HuespedEmail`/`AgenteEmail`)
- `src/Servicios/Reservas/Reservas.Application/Reservas/SolicitarCancelacion/SolicitarCancelacionCommandHandler.cs` (puebla emails)
- `src/Servicios/Reservas/Reservas.Application/Reservas/CancelarEnUnPaso/CancelarEnUnPasoCommandHandler.cs` (puebla emails)
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/ConsumidorReservaConfirmada.cs` (usa `EnvioIdempotenteCorreos`)
- `src/Servicios/Notificaciones/Notificaciones.Worker/Program.cs` (registra el consumidor)

**Nuevos (tests):**
- `tests/Notificaciones.UnitTests/ConsumidorSolicitudCancelacionTests.cs`

**Modificados (tests):**
- `tests/Contracts/ContratoSolicitudCancelacionRegistradaTests.cs` (emails en el contrato)
- `tests/Reservas.UnitTests/Reservas/SolicitarCancelacion/SolicitarCancelacionCommandHandlerTests.cs` (test del emisor)
- `tests/Notificaciones.IntegrationTests/InboxIdempotenciaRedisTests.cs` (margen TTL)

### Change Log

- 2026-07-09 — Party-mode (5.2/5.3): decisión de ruteo de emails = enriquecer los eventos de cancelación (opción a, unánime).
- 2026-07-09 — Ciclo A: enriquecimiento aditivo de `SolicitudCancelacionRegistrada.v1` + emisores 4.1/4.3 lo pueblan. Red→Green.
- 2026-07-09 — Ciclo B: `ConsumidorSolicitudCancelacion` (acuse estimación + aviso por resolver) sobre `EnvioIdempotenteCorreos` (helper extraído); refactor de `ConsumidorReservaConfirmada`. Red→Green.
- 2026-07-09 — Regresión completa (377 tests) verde + `dotnet format` limpio; Status → review.
