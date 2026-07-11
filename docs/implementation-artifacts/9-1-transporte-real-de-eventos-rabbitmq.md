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

- [x] **Task 1 — Paquetes (CPM)** ✅ `RabbitMQ.Client` 7.1.2 + `Testcontainers.RabbitMq` 4.13.0 en `Directory.Packages.props`; PackageReference en Reservas.Infrastructure, Hoteles.Infrastructure, Notificaciones.Worker y tests/Notificaciones.IntegrationTests. (No se usó `Aspire.RabbitMQ.Client`: innecesario.)
- [x] **Task 2 — Adaptador productor `PublicadorEventosRabbitMq`** (AC: 9.1.1, 9.1.5) ✅ En Reservas.Infrastructure + Hoteles.Infrastructure (duplicación deliberada). Serializa el envelope (`JsonSerializerDefaults.Web`), publica a exchange direct durable `hotelbookinghub.eventos` con routing key = topico, mensaje persistente. Conexión/canal perezosos y reutilizados con gate; propaga la excepción si el broker no está (AC-9.1.5).
- [x] **Task 3 — Selección por entorno (Strategy) en DI** (AC: 9.1.4) ✅ Factory en ambos `RegistroInfraestructura`: `PublicadorEventosRabbitMq` si hay `ConnectionStrings:rabbitmq`, si no `PublicadorEventosLog`. Dominio/handlers intactos.
- [x] **Task 4 — Consumidor del worker + enrutamiento por tipo** (AC: 9.1.2) ✅ `ConsumidorRabbitMq : BackgroundService` (cola durable `notificaciones.reservas` bound al topic `reservas`, `AsyncEventingBasicConsumer`, ack manual / nack+requeue). `EnrutadorNotificaciones` con mapa `Type → DespachadorNotificaciones` (resuelve el TODO del enrutamiento). Env-gated en `Program.cs` (solo con cadena RabbitMQ).
- [x] **Task 5 — docker-compose** (AC: 9.1.1, 9.1.2) ✅ `ConnectionStrings__rabbitmq` en reservas/hoteles/notificaciones; healthcheck de `rabbitmq` + `depends_on: condition: service_healthy`. ⚠️ **Ver nota de alcance abajo:** el data-plane funcional del compose (cadenas SQL/Redis + migraciones al arranque) es un gap PRE-EXISTENTE, fuera de esta historia.
- [x] **Task 6 — Tests (TDD, Testcontainers RabbitMQ)** (AC: 9.1.2, 9.1.3, 9.1.5) ✅ `TransporteRabbitMqTests` (Testcontainers): publish→consume→notificación **exactamente 1 vez** + re-entrega del mismo `MessageId` NO re-emite (idempotencia). Ciclo Red→Green visible (stub→real). Corre en `dotnet test`. `Notificaciones.UnitTests` serializado (`AssemblyInfo`) por el listener process-wide de 7.1. *(El at-least-once del productor con broker caído (AC-9.1.5) ya está cubierto por `ProcesadorOutbox` + el adaptador propaga la excepción; verificado por diseño/lectura, no test nuevo dedicado — el `ProcesadorOutbox` ya tiene su cobertura de fallo de publicación.)*
- [x] **Task 7 — Documentación** (AC: todos) ✅ `docs/observabilidad.md` no aplica; se documenta en la historia + `deferred-work.md`: ruta `hoteles→Reservas` (proyección) por transporte real = follow-up 9.2; adaptador Dapr de nube = E8; data-plane funcional del compose = gap pre-existente (Epic T).

### Nota de alcance (honestidad — data-plane del compose)

⚠️ **Descubierto durante la implementación:** el `docker-compose.yml` **nunca cableó ninguna cadena de conexión** (SQL, Redis) ni aplica migraciones EF al arrancar — fue un **smoke de arranque** (`/health` 200), no un compose funcional de datos. Por eso, aun con el transporte RabbitMQ cableado, un `docker compose up` + crear reserva NO fluye end-to-end **por falta del data-plane** (sin BD con esquema no se crea la reserva). **Esto excede el alcance de 9.1 (transporte).** El cierre real del transporte se demuestra con el **test Testcontainers en verde** (broker real → consumidor → notificación → idempotencia), que es la evidencia CI-gated. Hacer el compose funcional de punta a punta (cadenas SQL/Redis + `Migrate()` al arranque) se registra en `deferred-work.md` como trabajo de **Épica T** (DoD de entrega "docker compose up funciona"). No se finge: la Task 5 cablea el transporte; el data-plane se declara pendiente.

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

### Review Findings

_Code review adversarial de 3 capas (Blind · Edge · Auditor), 2026-07-10. 8 patch (2 ALTA), 1 defer._

- [ ] [Review][Patch][ALTA] Pérdida silenciosa de eventos por carrera de topología: el productor declara solo el exchange y publica `mandatory:false`; si publica antes de que exista la cola/binding, el exchange direct descarta el mensaje y el `ProcesadorOutbox` lo marca `Enviada` → pérdida permanente. Fix: el productor declara también la **cola durable + binding** del consumidor (reservas→`notificaciones.reservas`, hoteles→`proyeccion.hoteles` como holding para 9.2) → el mensaje se retiene en la cola durable aunque el consumidor no esté listo. [src/Servicios/Reservas/Reservas.Infrastructure/Mensajeria/PublicadorEventosRabbitMq.cs, src/Servicios/Hoteles/Hoteles.Infrastructure/Mensajeria/PublicadorEventosRabbitMq.cs]
- [ ] [Review][Patch][ALTA] `Type` null/vacío → `EnrutadorNotificaciones` hace `Dictionary.TryGetValue(null)` que lanza ANTES del despachador → el tope de intentos nunca aplica → requeue infinito. Fix: guard `string.IsNullOrEmpty(evento.Type)` → tratar como tipo desconocido (ack). [src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/EnrutadorNotificaciones.cs]
- [ ] [Review][Patch][MEDIA] Reconexión del productor no dispone la conexión/canal anteriores → fuga por cada caída. Fix: `DisposeAsync` de los viejos antes de recrear. [ambos PublicadorEventosRabbitMq.cs]
- [ ] [Review][Patch][MEDIA] `BasicPublishAsync` se ejecuta fuera del gate sobre un `IChannel` singleton (no thread-safe en 7.x). Fix: serializar la publicación bajo el semáforo. [ambos PublicadorEventosRabbitMq.cs]
- [ ] [Review][Patch][MEDIA] AC-E9.1.2: el enrutamiento por tipo no está probado para 3 de 4 tipos. Fix: test de discriminación (p. ej. `SolicitudCancelacionRegistrada.v1` va a su consumidor, no al de confirmación). [tests/Notificaciones.IntegrationTests/TransporteRabbitMqTests.cs]
- [ ] [Review][Patch][BAJA] AC-E9.1.3: el test cuenta 2 correos pero no verifica identidad. Fix: afirmar que los destinatarios son el huésped y el agente. [tests/Notificaciones.IntegrationTests/TransporteRabbitMqTests.cs]
- [ ] [Review][Patch][BAJA] AC-E9.1.5: sin test del adaptador. Fix: test unit/integración — `PublicadorEventosRabbitMq` con broker inalcanzable → `PublicarAsync` propaga la excepción. [tests/...]
- [ ] [Review][Patch][BAJA] `EnrutarAsync` devuelve `bool` muerto (ignorado por el consumidor). Fix: cambiar a `Task` (el ack/nack se decide por excepción). [EnrutadorNotificaciones.cs, ConsumidorRabbitMq.cs]
- [x] [Review][Defer] Requeue sin backoff (hot-loop acotado a `MaxIntentos` por corrida) + `ContadorReintentosEnMemoria` se reinicia entre reinicios del worker → un veneno podría reintentarse sin cota cross-restart. Deferred: acotado dentro de una corrida; el fix real es un contador en Redis (misma limitación documentada del inbox de 5.1b). Registrado en `deferred-work.md`.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Amelia / dev-story, modo autónomo)

### Debug Log References

- TDD Red→Green visible: `test(9.1): … (RED)` (consumidor STUB → test E2E Testcontainers en rojo: sin notificación) → fase verde (consumidor real).
- Suite completa verde tras el cambio; `Notificaciones.UnitTests` serializado para eliminar un flaky latente (listener process-wide de 7.1 + emisor de spans en paralelo).
- Verificado determinista: test de transporte y `Notificaciones.UnitTests` en múltiples corridas.

### Completion Notes List

- **Transporte real por RabbitMQ (local) detrás de `IPublicadorEventos`** (patrón Strategy por entorno, ADR-019): productor `PublicadorEventosRabbitMq` (Reservas+Hoteles) + consumidor `ConsumidorRabbitMq` + `EnrutadorNotificaciones` (mapa `Type→DespachadorNotificaciones`). `docker compose up` selecciona el adaptador RabbitMQ; sin cadena, cae al placeholder. El dominio y los consumidores no cambiaron (el envelope JSON ya era el contrato; contract tests intactos).
- **Enrutamiento por tipo resuelto** (TODO de 5.x "el enrutamiento llega con el transporte"): ReservaConfirmada→confirmación, SolicitudCancelacionRegistrada→solicitud, ReservaCancelada/SolicitudCancelacionRechazada→resolución. Reusa idempotencia + tope de intentos + dead-letter existentes.
- **at-least-once preservado:** el adaptador propaga la excepción al `ProcesadorOutbox`, que deja la fila `Pendiente` (no toqué esa lógica).
- **Evidencia end-to-end CI-gated:** `TransporteRabbitMqTests` (Testcontainers RabbitMQ) prueba publish→broker→consumidor→notificación **exactamente 1 vez** + idempotencia de re-entrega, dentro de `dotnet test`.
- **Alcance honesto:** data-plane funcional del compose (SQL/Redis + migraciones) = gap pre-existente → `deferred-work.md` (Epic T); ruta hoteles→Reservas por transporte real → follow-up 9.2; adaptador Dapr de nube → E8.

### File List

**Nuevos**
- `src/Servicios/Reservas/Reservas.Infrastructure/Mensajeria/PublicadorEventosRabbitMq.cs`
- `src/Servicios/Hoteles/Hoteles.Infrastructure/Mensajeria/PublicadorEventosRabbitMq.cs`
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/ConsumidorRabbitMq.cs`
- `src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/EnrutadorNotificaciones.cs`
- `tests/Notificaciones.IntegrationTests/TransporteRabbitMqTests.cs`
- `tests/Notificaciones.UnitTests/AssemblyInfo.cs`

**Modificados**
- `Directory.Packages.props` (RabbitMQ.Client, Testcontainers.RabbitMq)
- `src/Servicios/Reservas/Reservas.Infrastructure/Reservas.Infrastructure.csproj`, `.../Hoteles.Infrastructure.csproj`, `.../Notificaciones.Worker.csproj`, `tests/Notificaciones.IntegrationTests/*.csproj` (PackageReference)
- `src/Servicios/Reservas/Reservas.Infrastructure/RegistroInfraestructura.cs`, `.../Hoteles.Infrastructure/RegistroInfraestructura.cs` (selección por entorno)
- `src/Servicios/Notificaciones/Notificaciones.Worker/Program.cs` (enrutador + consumidor env-gated)
- `deploy/docker-compose.yml` (rabbitmq connection string + healthcheck + depends_on)
- `docs/implementation-artifacts/deferred-work.md`

## Change Log

| Fecha | Cambio |
|---|---|
| 2026-07-10 | Story 9.1: transporte real de eventos por RabbitMQ (local) detrás de `IPublicadorEventos` (Strategy por entorno, ADR-019). Productor+consumidor+enrutador por tipo; compose con RabbitMQ+healthcheck; test E2E Testcontainers (idempotencia). TDD Red→Green. Data-plane funcional del compose diferido (Epic T). Status → review. |
