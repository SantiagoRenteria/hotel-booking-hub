---
baseline_commit: 671fb00db9effda4f33aae326a67b4902d71ad21
---
# Story 9.1: Transporte real de eventos por RabbitMQ (local)

Status: in-progress

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

> **Trazabilidad:** correct-course (party-mode + Santiago, 2026-07-10) → **FR-19…21 · NFR-3 (ejecución real)** → `AC-E9.1.x` · **Obligatorio (cierre de brecha)**
> **Porqué:** un evaluador corre `docker compose up`, crea una reserva y espera ver la notificación; hoy muere en un log (`PublicadorEventosLog`). Cerrar el cable convierte el diferenciador de mensajería de "documentado + probado en costuras" a "corre end-to-end". Ver [[transporte-eventos-strategy-decision]], ADR-019, ADR-020.

## Story

Como **operador**,
quiero que **los eventos de dominio viajen por un broker real (RabbitMQ) del productor al worker en el entorno local**,
para **que las notificaciones se disparen de verdad al confirmar/cancelar una reserva, sin acoplar el dominio al transporte**.

## Acceptance Criteria

**AC-E9.1.1 — Publicación real detrás del puerto**
**Dado** un evento encolado en el Outbox (p. ej. `ReservaConfirmada.v1`)
**Cuando** el `RelayOutbox`/`ProcesadorOutbox` lo procesa en el entorno local
**Entonces** se publica a RabbitMQ vía un adaptador `PublicadorEventosRabbitMq` (implementación de `IPublicadorEventos`) al exchange por `topico` (`reservas`/`hoteles`), sin que el dominio ni el `ProcesadorOutbox` cambien.

**AC-E9.1.2 — Consumo real por el worker + enrutamiento por tipo**
**Dado** un evento publicado en el topic `reservas`
**Cuando** el `Notificaciones.Worker` está corriendo con transporte configurado
**Entonces** un `BackgroundService` consumidor lo recibe, lo deserializa al envelope `EventoIntegracion`, y lo **enruta por `evento.Type`** al `IProcesadorEvento` correcto (`ReservaConfirmada.v1`→`ConsumidorReservaConfirmada`; `SolicitudCancelacionRegistrada.v1`→`ConsumidorSolicitudCancelacion`; `ReservaCancelada.v1`/`SolicitudCancelacionRechazada.v1`→`ConsumidorResolucionCancelacion`), pasando por el `DespachadorNotificaciones` existente (tope de intentos + dead-letter).

**AC-E9.1.3 — End-to-end verificable (Testcontainers), exactamente 1 efecto**
**Dado** un test de integración con **Testcontainers RabbitMQ**
**Cuando** se publica un `ReservaConfirmada.v1` por `PublicadorEventosRabbitMq` y el consumidor lo procesa
**Entonces** el notificador recibe **exactamente los correos esperados** (huésped + agente); una **re-entrega del mismo `MessageId`** NO los re-emite (dedup por el inbox idempotente existente); el test corre dentro de `dotnet test` (no smoke manual).

**AC-E9.1.4 — Selección por entorno (Strategy) sin tocar el dominio**
**Dado** el registro DI
**Cuando** hay cadena de conexión de RabbitMQ configurada (local/compose)
**Entonces** se selecciona `PublicadorEventosRabbitMq` (productor) y se arranca el consumidor RabbitMQ (worker); **cuando NO** hay cadena (p. ej. tests unit sin broker), se cae al placeholder `PublicadorEventosLog` y el worker no arranca el consumidor. El puerto `IPublicadorEventos`, los handlers y el dominio **no** cambian (el adaptador Dapr de nube se añadirá en E8 por el mismo seam).

**AC-E9.1.5 (negativo) — Broker caído no pierde el evento**
**Dado** RabbitMQ no disponible al publicar
**Cuando** el `ProcesadorOutbox` intenta publicar
**Entonces** la excepción se captura, el mensaje **permanece `Pendiente`** en el Outbox (no se marca `Enviada`) y se re-reclama al vencer el lease (at-least-once). *(Este comportamiento ya existe en `ProcesadorOutbox`; el AC verifica que el adaptador RabbitMQ propaga la excepción en vez de tragarla.)*

## Alcance (leer antes de implementar)

- **Solo transporte LOCAL por RabbitMQ.** El adaptador **Dapr → Azure Service Bus** y **Dapr Secrets/Key Vault** son de la **Épica 8** (nube), por el mismo puerto (ADR-019/020). NO se cablea Dapr CLI, sidecars ni `Dapr.Client` aquí.
- **Foco: la ruta de notificaciones** (productor Reservas → topic `reservas` → `Notificaciones.Worker`), que es el flujo headline "crear reserva → notificación". El adaptador productor se registra también en **Hoteles** (mismo swap, publica al topic `hoteles`) porque es el mismo cambio y deja el sistema coherente.
- **Fuera de alcance (follow-up, anotar en `deferred-work.md`):** el consumo real del topic `hoteles` por el servicio **Reservas** para alimentar `ProyeccionHabitacion` (`ProyectorCatalogo`, `IConsumidorEventosCatalogo`) — su handler existe y está probado; cablear su transporte es el MISMO patrón aplicado al servicio Reservas (candidato a Story 9.2). No bloquea la ruta de notificaciones.

## Tasks / Subtasks

- [ ] **Task 1 — Paquetes (CPM)** (AC: todos)
  - [ ] Añadir a `Directory.Packages.props`: `RabbitMQ.Client` (7.x) y `Testcontainers.RabbitMq` (alinear versión con `Testcontainers.MsSql`/`.Redis` = 4.13.0). Prohibido versionar en el `.csproj` (CPM). `Aspire.RabbitMQ.Client` es **opcional** (wiring de `IConnection` con health/telemetría) — solo si simplifica; no imprescindible.
- [ ] **Task 2 — Adaptador productor `PublicadorEventosRabbitMq`** (AC: 9.1.1, 9.1.5)
  - [ ] Implementar `IPublicadorEventos` en `Reservas.Infrastructure/Mensajeria/` (y homólogo en `Hoteles.Infrastructure/Mensajeria/` — duplicación deliberada, coherente con el outbox relay por BC). Serializar el envelope con `new JsonSerializerOptions(JsonSerializerDefaults.Web)` (idéntico a `ProcesadorOutbox`), publicar a un **topic/direct exchange durable** con routing key = `topico`, mensaje **persistente**. Propagar excepción si el broker no está (NO tragarla → AC-E9.1.5).
  - [ ] Conexión robusta: declarar exchange idempotente al arrancar; retry/reconexión (Polly ya está en el stack de resiliencia). No compartir `IChannel` entre hilos.
- [ ] **Task 3 — Selección por entorno (Strategy) en DI** (AC: 9.1.4)
  - [ ] En `RegistroInfraestructura.AddReservasInfrastructure` (línea 58, hoy `AddSingleton<IPublicadorEventos, PublicadorEventosLog>`) y el homólogo de Hoteles: seleccionar `PublicadorEventosRabbitMq` si hay `ConnectionStrings:rabbitmq` (o `messaging`); si no, mantener `PublicadorEventosLog`. Mismo patrón "Redis-si-configurado" del worker (`Program.cs` líneas 21-31).
- [ ] **Task 4 — Consumidor del worker (`BackgroundService`) + enrutamiento por tipo** (AC: 9.1.2)
  - [ ] Nuevo `ConsumidorRabbitMq : BackgroundService` en `Notificaciones.Worker`: conecta a RabbitMQ, declara una **cola durable** bound al topic `reservas`, consume, deserializa JSON→`EventoIntegracion` (mismo `JsonSerializerDefaults.Web`), **enruta por `evento.Type`** al `IProcesadorEvento` correspondiente y lo entrega vía `DespachadorNotificaciones`. **Ack manual** solo tras éxito; si `DespachadorNotificaciones` relanza (aún hay presupuesto), `nack`+requeue; si agotó el tope, ya hizo dead-letter+return → `ack`.
  - [ ] Registrar un **mapa `Type → IProcesadorEvento`** (resuelve el TODO "el enrutamiento por tipo de evento llega con el transporte", `Program.cs` línea 37). Registrar los 3 consumidores por su tipo. Cada uno se envuelve en un `DespachadorNotificaciones` propio (o el despachador recibe el consumidor resuelto por tipo).
  - [ ] Env-gated: arrancar el consumidor solo si hay cadena RabbitMQ; si no, el worker mantiene su latido actual (`Worker.cs`).
- [ ] **Task 5 — docker-compose** (AC: 9.1.1, 9.1.2)
  - [ ] Pasar `ConnectionStrings__rabbitmq` (p. ej. `amqp://guest:guest@rabbitmq:5672`) a `reservas`, `hoteles`, `notificaciones`. Añadir **healthcheck** a `rabbitmq` + `depends_on: condition: service_healthy` en los servicios que publican/consumen (evita pérdida de mensajes/errores al arranque). RabbitMQ default guest/guest en local NO es un secreto de producción (documentar en ADR-020/README); en nube va por Dapr+Key Vault.
- [ ] **Task 6 — Tests (TDD, Testcontainers RabbitMQ)** (AC: 9.1.2, 9.1.3, 9.1.5)
  - [ ] Añadir `Testcontainers.RabbitMq` + `RabbitMQ.Client` al `.csproj` de `tests/Notificaciones.IntegrationTests` (o un proyecto de integración nuevo si se prefiere aislar). **Red→Green visible.**
  - [ ] **E2E round-trip:** levantar RabbitMQ (Testcontainers), publicar `ReservaConfirmada.v1` por `PublicadorEventosRabbitMq`, arrancar `ConsumidorRabbitMq`, afirmar que el `NotificadorFake`/inbox recibió los correos esperados (huésped+agente) **exactamente 1 vez**; **re-entrega del mismo `MessageId` NO re-emite** (dedup por `InboxIdempotenciaEnMemoria`).
  - [ ] **Enrutamiento:** un `SolicitudCancelacionRegistrada.v1` va a `ConsumidorSolicitudCancelacion`, no al de confirmación.
  - [ ] **At-least-once productor (AC-E9.1.5):** con un publicador que lanza (broker caído simulado) o RabbitMQ detenido, `ProcesadorOutbox.ProcesarLoteAsync` deja la fila `Pendiente` y devuelve 0 enviadas. *(Puede ser unit con fake que lanza; ya hay precedente `PublicadorEventosLog` fake.)*
  - [ ] Aislamiento xUnit: los tests que levantan contenedor van en collection propia (patrón de las integraciones existentes, `RedisFixture`/`WorkerG3Tests`).
- [ ] **Task 7 — Documentación** (AC: todos)
  - [ ] Actualizar `docs/observabilidad.md`/README-relevante y `deferred-work.md`: la ruta `hoteles→Reservas` (proyección) por transporte real queda como follow-up (9.2); el adaptador Dapr de nube queda en E8.

## Dev Notes

### Estado actual del código que se toca (leer, no asumir)

- **Puerto:** `src/Comun/HotelBookingHub.Comun/Eventos/IPublicadorEventos.cs` → `Task PublicarAsync(string topico, EventoIntegracion evento, CancellationToken ct)`. **Comun NO puede depender de `RabbitMQ.Client`** (su `.csproj` solo referencia abstracciones DI/Logging/FluentValidation, regla de frontera). Por eso el adaptador vive en cada `*.Infrastructure`, no en `Comun`.
- **Productor Reservas:** `Reservas.Infrastructure/Outbox/ProcesadorOutbox.cs` — `Topico = "reservas"`; deserializa `mensaje.Payload`→`EventoIntegracion` con `JsonSerializerDefaults.Web` y llama `publicador.PublicarAsync`. **Persiste el intento ANTES de publicar y solo marca `Enviada` tras éxito** (at-least-once ya garantizado; NO tocar esta lógica). Homólogo Hoteles: `Topico = "hoteles"`.
- **Registro DI a cambiar:** `Reservas.Infrastructure/RegistroInfraestructura.cs:58` (`AddSingleton<IPublicadorEventos, PublicadorEventosLog>`) y el equivalente en `Hoteles.Infrastructure`. Solo cambia la SELECCIÓN del adaptador; el resto del registro queda igual.
- **Worker:** `Notificaciones.Worker/Program.cs` registra `ConsumidorReservaConfirmada` como `IProcesadorEvento` + `ConsumidorSolicitudCancelacion`/`ConsumidorResolucionCancelacion` como singletons, `DespachadorNotificaciones`, inbox idempotente (Redis-si-configurado, si no en memoria). Comentario línea 37: *"El enrutamiento por tipo de evento llega con el transporte"* → **esta historia lo resuelve.** `Worker.cs` solo late.
- **Consumidores:** cada `IProcesadorEvento` (`ConsumidorReservaConfirmada`, etc.) ya filtra por `evento.Type` y **deserializa `evento.Data` desde `JsonElement`** (patrón tras el transporte, ya implementado — ver `ConsumidorReservaConfirmada.Deserializar`). O sea, **el envelope JSON round-trip es exactamente el contrato que esperan**; no hay que cambiar los consumidores.
- **Despacho con dead-letter:** `DespachadorNotificaciones.DespacharAsync` — relanza si hay presupuesto (para reentrega), hace dead-letter+ack al agotar el tope. El `ack`/`nack` de RabbitMQ debe alinearse con esa semántica.
- **Envelope:** `EventoIntegracion(Id, Type, Version, OccurredAt, TraceId, Data)`. `Id` = `MessageId` (dedup key). Contract tests en `tests/Contracts` fijan la forma JSON — **no romper el contrato** (el adaptador serializa el mismo envelope).

### Arquitectura y ADRs

- **ADR-019 (transporte Strategy por entorno):** RabbitMQ local / Dapr→Service Bus nube, detrás de `IPublicadorEventos`. Esta historia implementa la rama local.
- **ADR-020 (secretos por entorno):** RabbitMQ local por `ConnectionStrings:rabbitmq` (env/compose), guest/guest local no es secreto de prod; Key Vault en nube.
- **ADR-004/018 (outbox + atomicidad):** NO tocar la transacción única dominio+outbox ni el orden. El transporte es aguas abajo del outbox.
- **Envelope + semver + at-least-once + dedup en el consumidor** (concurrency-and-messaging.md, ADR-002): la no-duplicación se garantiza en el **efecto** (inbox idempotente), no en el wire. Mantener.
- **Frontera Comun:** el adaptador de transporte NO va en `Comun` (arrastraría `RabbitMQ.Client`). Va en `*.Infrastructure`. Duplicación deliberada Reservas/Hoteles aceptada (precedente: outbox relay por BC).

### Versiones (CPM)

- **NUEVO** en `Directory.Packages.props`: `RabbitMQ.Client` 7.x (API async `IChannel`/`CreateConnectionAsync`), `Testcontainers.RabbitMq` 4.13.0 (alinear con MsSql/Redis). `Aspire.Hosting.RabbitMQ` 13.4.6 ya existe (AppHost). Opcional cliente: `Aspire.RabbitMQ.Client`.
- **Trampa RabbitMQ.Client 7.x:** API es **async** (`CreateConnectionAsync`, `CreateChannelAsync`, `BasicPublishAsync`); el consumidor usa `AsyncEventingBasicConsumer`. No mezclar con la API vieja síncrona de ejemplos 6.x.

### Testing standards

- xUnit + FluentAssertions; `TreatWarningsAsErrors` (0 warnings); `dotnet format` antes de commitear. Analizadores xUnit estrictos (xUnit2031: no `.Where` antes de `Assert.Single` → usar sobrecarga con predicado; xUnit2020: `Assert.Fail`, no `Assert.True(false)`).
- **Testcontainers** ya es el patrón del repo (MsSql, Redis). Reusar el estilo de `RedisFixture`/collections aisladas. El contenedor RabbitMQ tarda en estar healthy → marcar como integración, `DisableParallelization` en su collection.
- **Determinismo del consumo:** el consumo es asíncrono → usar espera acotada (poll/`TaskCompletionSource` con timeout), NO `Task.Delay` fijo a ciegas (lección de 7.2).
- **NO depender de `docker compose up` para el gate de CI:** el smoke de compose solo corre en PRs a `main`. El gate real es el test Testcontainers dentro de `dotnet test`.

### Continuidad / lecciones previas

- **Honestidad de checkbox** (retro E6, review 7.1/7.2): calificar cada task con estado real; lo diferido (ruta hoteles→Reservas, Dapr nube) va explícito en `deferred-work.md`, no fingido.
- **TDD Red→Green visible** en commits (`test:` rojo → `feat:` verde) — historia crítica (infra de mensajería real, concurrencia, at-least-once). Aplica también el espíritu BDD de los AC (Given/When/Then observable).
- **PR-por-historia:** rama `feature/9-1-transporte-real-de-eventos-rabbitmq` → PR a `develop` → CI verde → merge. NO commits directos de código a develop (ver [[next-up-3-1-dev-story]]).

### Project Structure Notes

- Nuevos: `Reservas.Infrastructure/Mensajeria/PublicadorEventosRabbitMq.cs`, `Hoteles.Infrastructure/Mensajeria/PublicadorEventosRabbitMq.cs`, `Notificaciones.Worker/Notificaciones/ConsumidorRabbitMq.cs` (+ mapa de enrutamiento por tipo). Tests en `tests/Notificaciones.IntegrationTests` (o proyecto nuevo).
- Modificados: `RegistroInfraestructura.cs` (×2 BC, selección de adaptador), `Notificaciones.Worker/Program.cs` (registro consumidor + mapa por tipo), `Directory.Packages.props`, `deploy/docker-compose.yml`.
- **Variance vs enunciado:** el enunciado/ADR-002 hablaban de Dapr; esta historia usa RabbitMQ directo por decisión de ADR-019 (Strategy por entorno). No es desviación silenciosa: está en ADRs + Sprint Change Proposal.

### References

- [Source: docs/planning-artifacts/epics.md#Epic-9 / #Story-9.1]
- [Source: docs/specs/spec-hotel-booking-hub/decisions-adr.md#ADR-019 / #ADR-020]
- [Source: docs/specs/spec-hotel-booking-hub/concurrency-and-messaging.md] — outbox + idempotencia + broker intercambiable
- [Source: docs/planning-artifacts/sprint-change-proposal-2026-07-10.md]
- [Source: src/Servicios/Reservas/Reservas.Infrastructure/Outbox/ProcesadorOutbox.cs, RegistroInfraestructura.cs:58]
- [Source: src/Servicios/Notificaciones/Notificaciones.Worker/Program.cs:37, Notificaciones/DespachadorNotificaciones.cs, ConsumidorReservaConfirmada.cs]

## Dev Agent Record

### Agent Model Used

{{agent_model_name_version}}

### Debug Log References

### Completion Notes List

### File List
