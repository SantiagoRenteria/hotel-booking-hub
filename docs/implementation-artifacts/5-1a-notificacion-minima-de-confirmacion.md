---
baseline_commit: e2e50b2ae97f3baf0d8b4fc66aacdf1dcafa8b92
---

# Story 5.1a: Notificación mínima de confirmación (Fase 1)

Status: in-progress

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

- [ ] **Task 0 — Cableado del transporte productor→consumidor (decisión de arranque de E5)**
  - [ ] Definir cómo el `Notificaciones.Worker` recibe los eventos que el `RelayOutbox` de Reservas publica. Hoy `PublicadorEventosLog` es placeholder (solo loguea). Opciones: (a) Dapr pub/sub (como plantea la arquitectura, `AC-E1.2.x` cableó el salto async); (b) transporte in-proc/bus mínimo para Fase 1. **Escalar a party-mode SOLO si la elección de transporte abre una decisión arquitectónica** (Dapr sidecar vs alternativa); si el enabler de 1.2 ya dejó Dapr pub/sub cableado, reutilizarlo. Documentar la decisión aquí antes de implementar.
- [ ] **Task 1 — Abstracción `INotificador` + sink de Fase 1 (AC: 1)** *(TDD)*
  - [ ] `INotificador.NotificarAsync(destinatario, asunto, cuerpo)` en el worker; implementación de Fase 1 hacia consola/MailHog. Un correo al huésped y otro al agente (dos destinatarios).
- [ ] **Task 2 — Handler del evento `ReservaConfirmada.v1` (AC: 1)** *(TDD)*
  - [ ] Deserializar el envelope + data (`ReservaConfirmadaV1`), mapear a los correos (huésped: `HuespedEmail`; agente: `AgenteEmail`) y llamar a `INotificador`. Sin dedup todavía (5.1b).
- [ ] **Task 3 — Suscripción / consumo desde el worker (AC: 1)**
  - [ ] Conectar el handler al transporte de Task 0; el worker consume y dispara los correos.
- [ ] **Task 4 — Tests (unit + integración)**
  - [ ] Unit: el handler emite exactamente 2 correos (huésped + agente) con los campos del evento. Integración/E2E (según transporte): publicar `ReservaConfirmada` → el worker emite al sink (assert sobre el sink fake).
- [ ] **Task 5 — Commits TDD (Red→Green) en rama `feature/5-1a-notificacion-confirmacion` + PR a `develop`** (autor Santiago Renteria; sin trailers)

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

### Debug Log References

### Completion Notes List

### File List

### Change Log
