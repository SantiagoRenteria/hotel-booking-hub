# Story 5.1b: Worker idempotente sin pérdida ni duplicado (Fase 2)

Status: ready-for-dev

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

- [ ] **Task 1 — Inbox de idempotencia por `(MessageId, version)` (AC: 1)** *(TDD)*
  - [ ] Dedup **best-effort at-least-once** con **Redis `SETNX` + TTL** (NO tabla SQL: el envío SMTP es un efecto externo no transaccionable — party-mode 3.1 D3). Abstracción `IInboxIdempotencia` (`IntentarMarcarProcesadoAsync(messageId, version)`); si ya estaba marcado → se descarta el efecto.
  - [ ] **Orden del marcado vs efecto:** decidir y documentar (marcar-antes-de-enviar = at-most-once riesgo de pérdida; enviar-antes-de-marcar = at-least-once riesgo de duplicado). Para "exactamente-un-efecto" observable con reintentos, el patrón correcto y sus límites deben quedar explícitos (SETNX reserva; si el envío falla, liberar/expirar para reintento sin duplicar).
- [ ] **Task 2 — Envío exactamente-una-vez bajo reentrega (AC: 1)** *(TDD)*
  - [ ] Entregar el mismo evento N veces → **1 solo** correo por destinatario. Test con contador de efecto (patrón `deliveries >= 1` / `efecto == 1`).
- [ ] **Task 3 — Supervivencia a la caída del broker (G3) (AC: 2)** *(integración/fault-injection)*
  - [ ] Reutilizar la colección `OutboxFaultInjection` de E1 encendiendo el assert de "0 efecto duplicado" y "0 pérdida": broker caído durante una ráfaga → al recuperarse, 100% entregado, 0 perdidos, 0 duplicados.
- [ ] **Task 4 — Dead-letter + tope de intentos (mensaje-veneno)**
  - [ ] Un evento que falle SIEMPRE al procesar no debe re-reclamarse sin cota ni bloquear el stream (paridad con el relay de E1/E2). Tope de intentos → dead-letter.
- [ ] **Task 5 — Tests (unit + integración Testcontainers/Redis)**
  - [ ] Idempotencia (N entregas → 1 efecto), G3 (broker caído→recuperado, sin pérdida ni duplicado), veneno→dead-letter. Redis real vía fixture (patrón `RedisFixture`).
- [ ] **Task 6 — Commits TDD (Red→Green) en rama `feature/5-1b-worker-idempotente` + PR a `develop`** (autor Santiago Renteria; sin trailers)

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

### Debug Log References

### Completion Notes List

### File List

### Change Log
