---
baseline_commit: 42a61eab23a06beb9b1da76c138866088a10a7c7
---

# Story 5.1b: Worker idempotente sin pérdida ni duplicado (Fase 2)

Status: review

<!-- Generado por bmad-create-story (lote Épica 5). Complejidad ALTA (idempotencia del consumidor + supervivencia
al broker G3). Salda la deuda [DEUDA-VERIF:E5] de E1 (AC-E1.6b.4). TDD Red→Green + tests G3 (fault-injection).
Depende de 5.1a (worker consumiendo ReservaConfirmada). -->

## Story

Como **huésped y agente**,
quiero **recibir el correo exactamente una vez aunque el evento se reintente o el broker caiga**,
para **no recibir duplicados ni perder la notificación**.

Salda la parte `AC-E5` que E1 dejó como deuda: el **efecto exactamente-una-vez** (dedup del consumidor) y la **supervivencia a la caída del broker** (G3). Reutiliza el patrón de inbox decidido en `AC-E3.1.4` (party-mode 3.1 D3).

## Acceptance Criteria

1. **AC-E5.1b.1 — Idempotencia del consumidor (salda la deuda de E1).**
   **Dado** el mismo evento entregado N veces (`deliveries >= 1`, at-least-once),
   **cuando** el worker lo procesa deduplicando por `(MessageId, version)` en **Redis (SETNX + TTL)** (inbox de `AC-E3.1.4`),
   **entonces** se envía **exactamente 1** correo por destinatario (`efecto == 1`).
2. **AC-E5.1b.2 — Sin pérdida tras caída del broker (G3).**
   **Dado** el broker caído durante una ráfaga,
   **cuando** se recupera,
   **entonces** el **100%** de los eventos pendientes se entrega; **0** correos perdidos.

## Tasks / Subtasks

- [x] **Task 1 — Inbox de idempotencia por `(MessageId, version)` (AC: 1)** *(TDD)*
  - [x] Dedup **best-effort at-least-once** con **Redis `SETNX` + TTL** (NO tabla SQL: el envío SMTP es un efecto externo no transaccionable — party-mode 3.1 D3). Abstracción `IInboxIdempotencia` (`IntentarMarcarProcesadoAsync(messageId, version, efecto)` + `LiberarAsync`); si ya estaba marcado → se descarta el efecto. Impl. `InboxIdempotenciaRedis` (`SET NX EX` vía `IConnectionMultiplexer`) + `InboxIdempotenciaEnMemoria` (fallback/doble de test).
  - [x] **Orden del marcado vs efecto (decidido y documentado):** **reservar-antes-de-enviar + liberar-si-falla** (SETNX reserva → envía → si falla, `DEL` para reintentar sin duplicar). Límites explícitos en el docstring de `IInboxIdempotencia`: (a) caída entre reservar y enviar = ventana at-most-once acotada por el TTL (se autocorrige por reentrega al expirar); (b) el TTL debe superar la ventana máxima de reentrega del broker. Efecto **por destinatario** para no duplicar el correo ya enviado ante un fallo parcial.
- [x] **Task 2 — Envío exactamente-una-vez bajo reentrega (AC: 1)** *(TDD)*
  - [x] Entregar el mismo evento N veces → **1 solo** correo por destinatario (`ConsumidorIdempotenteTests`: N entregas, concurrencia, fallo parcial del 2º correo, eventos distintos no colapsan).
- [x] **Task 3 — Supervivencia a la caída del broker (G3) (AC: 2)** *(integración/fault-injection)*
  - [x] `WorkerG3Tests` con Redis real: ráfaga con reentregas múltiples → 1 correo/destinatario; **broker caído durante la ráfaga → al recuperarse, 100% entregado, 0 perdidos, 0 duplicados**. Enciende el assert del lado CONSUMIDOR que E1 dejó como deuda (AC-E1.6b.4). *(Nota: el `OutboxFaultInjection` de E1 prueba el lado PRODUCTOR/SQL; el equivalente del consumidor se materializa como fault-injection sobre el `INotificador` + inbox Redis, sin colección SQL.)*
- [x] **Task 4 — Dead-letter + tope de intentos (mensaje-veneno)**
  - [x] `DespachadorNotificaciones` (tope `MaxIntentos` → `IColaDeadLetter`) + `IContadorReintentos` (`ContadorReintentosEnMemoria`, INCR atómico) + `ColaDeadLetterLog`. Veneno → tras el tope, ACK (no relanza) y aparta a dead-letter; antes del tope propaga para reentrega; éxito reinicia el conteo. `DespachadorNotificacionesTests`.
- [x] **Task 5 — Tests (unit + integración Testcontainers/Redis)**
  - [x] Unit (`Notificaciones.UnitTests`, 11): idempotencia + veneno→dead-letter. Integración (`Notificaciones.IntegrationTests`, 6): SETNX+TTL real + G3. Redis real vía `RedisFixture` (`IConnectionMultiplexer` + Testcontainers).
- [x] **Task 6 — Commits TDD (Red→Green) en rama `feature/5-1b-worker-idempotente` + PR a `develop`** (autor Santiago Renteria; sin trailers) — 3 ciclos Red→Green + docs; PR pendiente al cierre.

### Review Findings (bmad-code-review 2026-07-09 · Blind + Edge + Auditor)

- [ ] [Review][Patch] F1 — La liberación de compensación usa el `ct` (posiblemente cancelado en shutdown) y puede enmascarar la excepción original: si el `ct` está cancelado o `LiberarAsync` falla, la reserva queda colgada → correo perdido hasta el TTL. Liberar con `CancellationToken.None`, best-effort, sin ocultar la excepción original. [Notificaciones.Worker/Notificaciones/ConsumidorReservaConfirmada.cs]
- [ ] [Review][Patch] F2 — Un destinatario con fallo permanente aborta el bucle secuencial antes del 2º correo; el destinatario sano nunca se envía y el mensaje entero va a dead-letter. Intentar ambos efectos de forma independiente y propagar tras intentarlos. [Notificaciones.Worker/Notificaciones/ConsumidorReservaConfirmada.cs]
- [ ] [Review][Patch] F3 — `OpcionesDespachador.MaxIntentos` sin validación (0/negativo → dead-letter al primer fallo o reintento infinito). Guard `>= 1` en el record. [Notificaciones.Worker/Notificaciones/DespachadorNotificaciones.cs]
- [x] [Review][Defer] F4 — `InboxIdempotenciaEnMemoria` sin TTL/evicción: crecimiento de memoria no acotado y sin la autocorrección at-most-once del TTL de Redis. Fallback de dev; Redis es el camino real. [InboxIdempotenciaEnMemoria.cs] — deferred
- [x] [Review][Defer] F5 — Redis caído en `IntentarMarcarProcesadoAsync` se cuenta como intento de procesamiento → un mensaje válido puede acabar en dead-letter (falso veneno) durante un outage. [DespachadorNotificaciones.cs] — deferred
- [x] [Review][Defer] F6 — Race check-then-act en el tope de intentos → dead-letter duplicado bajo competing-consumers. [DespachadorNotificaciones.cs] — deferred
- [x] [Review][Defer] F7 — No existe variante Redis (`INCR`) de `IContadorReintentos`: asimetría con el inbox multi-instancia (veneno reintentado hasta MaxIntentos×N_instancias). [IContadorReintentos.cs] — deferred
- [x] [Review][Defer] F8 — Fallo del sink de dead-letter deja el contador sin reiniciar y re-bloquea el mensaje (sin política de fallback). [DespachadorNotificaciones.cs] — deferred
- [x] [Review][Defer] F9 — Caída del worker entre reservar y enviar: ventana de pérdida hasta el TTL (límite inherente documentado; no ejercida por test; el checkbox "0 perdidos" es del broker caído con worker vivo). [ConsumidorReservaConfirmada.cs] — deferred
- [x] [Review][Defer] F10 — PII (email del destinatario) puede filtrarse a los logs vía `ex.Message` en el sink de dead-letter. [ColaDeadLetterLog.cs] — deferred
- [x] [Review][Defer] F11 — `CancellationToken` no propagado a las operaciones Redis (limitación de SE.Redis; solo check al entrar). [InboxIdempotenciaRedis.cs] — deferred
- [x] [Review][Defer] F14 — `DespachadorNotificaciones` registrado en DI pero nunca invocado en runtime (transporte productor→worker diferido en todo el sistema). [Program.cs] — deferred
- [x] [Review][Defer] F16 — Contador de reintentos en memoria se reinicia al reiniciar el worker (crash-loop de veneno no llega a dead-letter). [ContadorReintentosEnMemoria.cs] — deferred

## Dev Notes

### Arquitectura y archivos a tocar

- **Worker:** extiende el handler de 5.1a con el inbox de idempotencia ANTES del efecto. `IInboxIdempotencia` sobre Redis (`IDistributedCache`/StackExchange.Redis, ya referenciado por la caché de 3.2).
- **Inbox compartido (concepto, no store único — party-mode 3.1 D3):** E3 (proyección) mete la dedup en la MISMA tx SQL (autoridad dura); **E5 usa Redis SETNX+TTL** (efecto externo no transaccional). Reutilizar el PATRÓN/abstracción, con el store adecuado al efecto.
- **G3 / fault-injection:** reutilizar `OutboxFaultInjectionTests`/`InterceptorFallaOutbox` de E1; el relay ya sobrevive a fallos del publicador — aquí se enciende el assert del lado CONSUMIDOR (0 duplicado / 0 pérdida).
- **Dead-letter:** deuda ya registrada del relay (sin dead-letter ni tope de intentos); cerrarla para el consumidor aquí.

### Previous story intelligence

- 5.1a dejó el worker consumiendo `ReservaConfirmada` y el `INotificador`; 5.1b añade la garantía de efecto único.
- La deuda `[DEUDA-VERIF:E5]` de 1.6b (AC-E1.6b.4): "el colapso a un solo efecto se verifica en E5". Aquí se cierra.

### Anti-patrones a evitar

- Tabla SQL de mensajes-procesados para el efecto SMTP (no transaccional; usar Redis SETNX+TTL).
- Marcar-procesado y luego fallar el envío sin política de reintento (pérdida silenciosa).
- Reintentar un mensaje-veneno sin cota (bloquea el stream).

### References

- [epics.md — Story 5.1b (AC-E5.1b.x), SALDA AC-E1.6b.4](../planning-artifacts/epics.md)
- [3.1 inbox de idempotencia (AC-E3.1.4, party-mode D3)](3-1-proyeccion-de-habitaciones-idempotente-y-ordenada.md)
- [1.6b outbox transaccional + deuda E5](1-6b-atomicidad-transaccional-reserva-outbox.md)
- [Story 5.1a](5-1a-notificacion-minima-de-confirmacion.md)

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (dev-story autónomo, ciclo Épica 5).

### Debug Log References

- Test G3 `Broker_caido...`: falla inicial por modelo de inyección irreal (fallo en huésped **y** agente). Como los envíos son secuenciales y el del huésped va primero, la fase "broker caído" siempre abortaba en el huésped sin llegar al agente, dejando su fallo pendiente para la fase de recuperación (falso positivo de "duplicado"). El comportamiento del consumidor es correcto (aborta en el 1er fallo → reentrega); se corrigió el test para inyectar el fallo solo en el **primer efecto** (huésped), modelando "todo envío falla durante la caída". Ver commit GREEN del ciclo 3.

### Completion Notes List

- **Idempotencia (AC-E5.1b.1):** patrón reservar(SETNX)→enviar→liberar-si-falla, con clave de efecto **por destinatario** `(MessageId, version, destinatario)`. Cierra el hallazgo diferido de 5.1a "exactamente-una-vez + fallo parcial de los 2 correos": un fallo del 2º correo reintenta solo el pendiente sin re-enviar el que ya salió.
- **Sin pérdida / G3 (AC-E5.1b.2):** liberar la reserva al fallar + TTL garantizan que la reentrega re-envíe lo interrumpido; probado con Redis real (broker caído→recuperado → 100% entregado, 0 perdidos, 0 duplicados).
- **Salda `[DEUDA-VERIF:E5]` / AC-E1.6b.4:** el colapso a un solo efecto observable bajo reentrega queda verificado aquí.
- **Dead-letter (Task 4):** cierra la deuda del mensaje-veneno del consumidor (paridad con el relay de E1/E2): tope de intentos → dead-letter + ACK, sin bloquear el stream.
- **Wiring:** inbox Redis si hay connection string `redis` (dedup válida entre instancias del worker), si no fallback en memoria (dev local). El transporte real productor→worker sigue diferido en todo el sistema (deuda de infra transversal ya registrada); el comportamiento se prueba a nivel de consumidor + Redis real.
- **Contador de reintentos en memoria (Task 4):** fiel en single-instance; la variante Redis `INCR` es la extensión natural al escalar a competing-consumers (misma nota que el inbox).
- **Regresión:** 367 tests verdes (unit + integración con Testcontainers), `dotnet format` limpio.

### File List

**Nuevos (src):**
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/IInboxIdempotencia.cs`
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/InboxIdempotenciaEnMemoria.cs`
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/InboxIdempotenciaRedis.cs`
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/IProcesadorEvento.cs`
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/IColaDeadLetter.cs`
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/ColaDeadLetterLog.cs`
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/IContadorReintentos.cs`
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/ContadorReintentosEnMemoria.cs`
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/DespachadorNotificaciones.cs`

**Modificados (src):**
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/ConsumidorReservaConfirmada.cs` (dedup por destinatario + `IProcesadorEvento`)
- `src/Servicios/Notificaciones/Notificaciones.Worker/Program.cs` (wiring inbox Redis/memoria + despachador + dead-letter)
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones.Worker.csproj` (StackExchange.Redis)
- `Directory.Packages.props` (pin StackExchange.Redis 2.7.27)
- `HotelBookingHub.slnx` (nuevo proyecto de integración)

**Nuevos (tests):**
- `tests/Notificaciones.UnitTests/ConsumidorIdempotenteTests.cs`
- `tests/Notificaciones.UnitTests/DespachadorNotificacionesTests.cs`
- `tests/Notificaciones.IntegrationTests/` (proyecto: `.csproj`, `RedisFixture.cs`, `InboxIdempotenciaRedisTests.cs`, `WorkerG3Tests.cs`)

**Modificados (tests):**
- `tests/Notificaciones.UnitTests/ConsumidorReservaConfirmadaTests.cs` (nuevo constructor del consumidor)

### Change Log

- 2026-07-09 — Ciclo 1 (Tasks 1+2): `IInboxIdempotencia` + `InboxIdempotenciaEnMemoria`; consumidor con dedup por destinatario (reservar→enviar→liberar). Red→Green.
- 2026-07-09 — Ciclo 2 (Task 4): `DespachadorNotificaciones` (tope de intentos → dead-letter) + contador + `ColaDeadLetterLog`. Red→Green.
- 2026-07-09 — Ciclo 3 (Tasks 3+5): `InboxIdempotenciaRedis` (SET NX EX) + proyecto `Notificaciones.IntegrationTests` (inbox Redis real + G3 fault-injection). Red→Green.
- 2026-07-09 — Regresión completa (367 tests) verde + `dotnet format` limpio; Status → review.
