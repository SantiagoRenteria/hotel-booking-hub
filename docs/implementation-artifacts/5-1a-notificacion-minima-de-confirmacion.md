---
baseline_commit: e2e50b2ae97f3baf0d8b4fc66aacdf1dcafa8b92
---

# Story 5.1a: Notificación mínima de confirmación (Fase 1)

Status: review

<!-- Generado por bmad-create-story (lote Épica 5). Complejidad NORMAL. Criterio OBLIGATORIO del enunciado
(HU2-5 mínimo). TDD Red→Green. Primer consumidor real de eventos de integración: cablea el salto async
productor→worker. NO requiere idempotencia todavía (eso es 5.1b). -->

## Story

Como **huésped y agente**,
quiero **recibir un correo cuando se confirma la reserva**,
para **tener constancia inmediata de la reserva**.

Cierra el criterio obligatorio HU2-5 (FR-19, mínimo) con bajo riesgo: el correo debe **dispararse** al confirmar; un servidor SMTP real es pulido opcional. Primer consumidor de `ReservaConfirmada.v1` (evento congelado en 1.3).

## Acceptance Criteria

1. **AC-E5.1a.1 — El correo se dispara (outcome obligatorio).**
   **Dado** una reserva confirmada (evento `ReservaConfirmada` en el outbox de E1),
   **cuando** el relay publica y `Notificaciones.Worker` consume,
   **entonces** `INotificador` emite un correo al **huésped** y otro al **agente** hacia el sink de Fase 1 (consola/MailHog), verificable en el test/demo. *(SMTP real = pulido opcional; no bloquea el criterio.)*

## Tasks / Subtasks

- [x] **Task 0 — Cableado del transporte productor→consumidor (decisión de arranque de E5)** — ✅ **RESUELTA por precedente (sin party-mode):** el transporte Dapr real es un **placeholder documentado en todo el sistema** (`PublicadorEventosLog` solo loguea; no hay suscriptor). El patrón establecido (E3, `IConsumidorEventosCatalogo`→`ProyectorCatalogo`) es un **consumidor-clase invocado con el envelope** (`ProcesarAsync(EventoIntegracion)`), ejercido directamente en tests; la suscripción Dapr real queda diferida de forma consistente. 5.1a sigue ese patrón. No abrió decisión arquitectónica nueva.
- [x] **Task 1 — Abstracción `INotificador` + sink de Fase 1 (AC: 1)** *(TDD)*
  - [x] `INotificador.NotificarAsync(destinatario, asunto, cuerpo, ct)` + `NotificadorConsola` (sink Fase 1 vía log). Dos destinatarios (huésped + agente).
- [x] **Task 2 — Handler del evento `ReservaConfirmada.v1` (AC: 1)** *(TDD)*
  - [x] `ConsumidorReservaConfirmada.ProcesarAsync` deserializa el envelope (tipo o `JsonElement`), mapea a `HuespedEmail`/`AgenteEmail` y emite 2 correos. Ignora otros tipos. Sin dedup (5.1b).
- [x] **Task 3 — Suscripción / consumo desde el worker (AC: 1)**
  - [x] `INotificador` + `ConsumidorReservaConfirmada` registrados en el DI del worker; suscripción Dapr real diferida (Task 0).
- [x] **Task 4 — Tests (unit)**
  - [x] El consumidor emite exactamente 2 correos (huésped + agente) con datos de la reserva; deserialización desde `JsonElement`; ignora otros tipos. Nuevo proyecto `tests/Notificaciones.UnitTests`.
- [x] **Task 5 — Commits TDD (Red→Green) en rama `feature/5-1a-notificacion-confirmacion` + PR a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Arquitectura y archivos a tocar

- **Worker:** `src/Servicios/Notificaciones/Notificaciones.Worker/` (hoy `Worker.cs` es un latido placeholder; `Program.cs` solo registra el `BackgroundService`). Añadir `INotificador` + sink de Fase 1 + handler del evento.
- **Evento consumido:** `ReservaConfirmadaV1` (`Comun.Eventos`) — contrato congelado en 1.3 (envelope `{id,type,version,occurredAt,traceId,data}`; dedup key = `id`; order key = `aggregateId`+`version`). Campos relevantes: `HuespedEmail`, `HuespedNombre`, `AgenteEmail`, `HotelNombre`, `Ciudad`, `Entrada`, `Salida`, `PrecioTotal`.
- **Transporte:** el `RelayOutbox` de Reservas publica vía `IPublicadorEventos` (hoy `PublicadorEventosLog`, placeholder). Ver Task 0.

### Anti-patrones a evitar

- Meter idempotencia/dedup aquí (es 5.1b; 5.1a solo debe DISPARAR el correo).
- Acoplar el worker al `DbContext` de Reservas (cruza frontera de BC; consume el EVENTO, no la BD).
- Depender de un SMTP real para cerrar el criterio (Fase 1 = sink consola/MailHog).

### References

- [epics.md — Story 5.1a (AC-E5.1a.1), regla de propiedad de eventos](../planning-artifacts/epics.md)
- [ReservaConfirmada.v1 contrato](1-3-contrato-del-evento-reserva-confirmada.md)
- [1.1 esqueleto del worker](1-1-esqueleto-ejecutable-de-un-comando.md)

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Amelia, dev-story) — 2026-07-09.

### Debug Log References

- Suite completa verde: 353 tests (3 nuevos en `Notificaciones.UnitTests`). TDD Red→Green visible (`test(5.1a): RED` → `feat(5.1a): GREEN`).

### Completion Notes List

- **Task 0 sin party-mode:** el transporte Dapr real es placeholder documentado en todo el sistema; el consumidor se modela como clase invocada con el envelope (patrón E3) y la suscripción real queda diferida consistentemente.
- **AC-E5.1a.1:** `ConsumidorReservaConfirmada` dispara 2 correos (huésped `HuespedEmail` + agente `AgenteEmail`) al sink de Fase 1 (`NotificadorConsola`), con datos de la reserva; deserializa `data` tanto tipado como `JsonElement`; ignora otros tipos de evento.
- **Sin dedup** (es 5.1b). Nuevo proyecto de test `tests/Notificaciones.UnitTests` añadido a la solución.
- **Diferido:** transporte Dapr real (suscripción); tests E2E productor→worker una vez exista el transporte.

### File List

**Worker (Notificaciones.Worker):**
- `Notificaciones/INotificador.cs` (A), `Notificaciones/NotificadorConsola.cs` (A), `Notificaciones/ConsumidorReservaConfirmada.cs` (A)
- `Program.cs` (M — registra INotificador + consumidor)

**Tests:** `tests/Notificaciones.UnitTests/Notificaciones.UnitTests.csproj` (A) + `ConsumidorReservaConfirmadaTests.cs` (A); `HotelBookingHub.slnx` (M — alta del proyecto).

### Change Log

- 2026-07-09 — Story 5.1a implementada (worker dispara correo de confirmación, Fase 1). Estado → review.
