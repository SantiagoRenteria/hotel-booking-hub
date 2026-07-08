# Concurrencia, anti-overbooking y mensajería

Compañero de [SPEC.md](SPEC.md). Sostiene CAP-6 (cero overbooking) y CAP-7 (notificación sin pérdida ni duplicación) y sus constraints.

## Overbooking — el problema central

El invariante ("no dos reservas solapadas de la misma habitación") se garantiza **a nivel de motor**, no con lógica de aplicación (frágil ante concurrencia). SQL Server no tiene *exclusion constraints* como PostgreSQL, así que se usa el **patrón de slots de inventario**: una fila por noche reservada con restricción de unicidad (ver DDL en [architecture-diagrams.md](architecture-diagrams.md)).

Al confirmar, dentro de **una transacción bajo READ COMMITTED**, se insertan las noches `[entrada, salida)`. Si **alguna** ya existe, la violación del índice `UNIQUE (HabitacionId, Noche)` (`SqlException` 2627/2601) aborta la transacción → **409 Conflict** (Problem Details RFC 7807), **sin reintento**. El índice es el árbitro: no hace falta `SERIALIZABLE` (ver ADR-016). Dos reservas concurrentes sobre la misma habitación y fechas: una gana, la otra falla limpio. Cero overbooking, garantizado por el motor, **portable** (SQL Server, PostgreSQL, etc.) y con soporte natural para disponibilidad parcial.

> Alternativas evaluadas y descartadas: **`SERIALIZABLE`** (más caro y más *deadlocks* bajo contención, sin beneficio dado que el índice único ya arbitra el conflicto — ADR-016), `sp_getapplock` por `HabitacionId`, y verificación de rango sin tabla de slots. Documentadas en ADR-003/ADR-016 ([decisions-adr.md](decisions-adr.md)).

## Todas las condiciones de carrera del sistema (resueltas por diseño)

| Condición de carrera | Mecanismo |
|----------------------|-----------|
| Overbooking (2 reservas, misma habitación/fechas) | Slots `NochesHabitacion` + `UNIQUE (HabitacionId, Noche)` arbitra en el INSERT (READ COMMITTED) → una gana, otra `409` |
| Edición concurrente (2 agentes, mismo hotel) | Concurrencia optimista con `rowversion` → `409` + recarga |
| Evento procesado dos veces (broker *at-least-once*) | Idempotencia: inbox en Redis por `message-id` |
| Doble publicación desde el outbox | *At-least-once* + idempotencia aguas abajo lo absorbe |
| Doble envío del cliente (doble clic en "reservar") | **Idempotency-Key** en header del `POST` → devuelve la misma reserva |
| *Deadlock* (SQL 1205) bajo contención | Retry acotado (3 intentos, backoff+jitter) solo ante 1205; `2627/2601` → `409` sin retry |
| Doble resolución de la misma solicitud (2 agentes / doble clic) | Guard de estado + `rowversion` → solo la primera resolución (aprobar o rechazar) gana; la segunda recibe `409` |
| Dos solicitudes de cancelación sobre la misma reserva (viajero + agente por teléfono) | Guard: una reserva con solicitud en curso rechaza una nueva → `409` |
| Liberar slots (aprobación) y reservar la misma habitación/fechas a la vez | La aprobación **borra físicamente** las filas `NocheHabitacion`; ese *delete* y el *insert* de la nueva reserva se serializan por el mismo índice `UNIQUE`; la nueva reserva solo puede confirmar cuando la liberación ya está *commit* |

## Consistencia y mensajería (Outbox + idempotencia + Dapr)

- **No perder el evento — Transactional Outbox:** la reserva, sus slots y su evento se escriben en la misma transacción ACID de SQL Server. Un relay lee la tabla `outbox` y publica vía Dapr. Si el broker está caído, el evento permanece en `outbox` hasta publicarse. Los eventos de cancelación (`SolicitudCancelacionRegistrada`, `ReservaCancelada`, `SolicitudCancelacionRechazada`) usan el **mismo mecanismo de outbox**: la transición de estado y su evento se escriben en la misma transacción — incluido el **rechazo** (que devuelve la reserva a `Confirmada`, sin tocar slots); la liberación de slots solo ocurre en la **aprobación**.
- **No procesar dos veces — idempotencia:** `Notificaciones.Worker` registra el `message-id` en Redis (con TTL); si llega repetido, lo ignora. Complementado con reintentos y **DLQ** de Dapr.
- **Broker intercambiable:** el mismo `Reservas.Api` publica a un *topic* Dapr; el *component* define el transporte — Local → RabbitMQ (docker-compose); Nube → Azure Service Bus. **Cero cambios de código**, solo el YAML del component.

## Dapr — building blocks utilizados

Modelo *sidecar*: cada servicio corre junto a un proceso Dapr que expone una API local; los sidecars hablan entre sí, desacoplando el código de la infraestructura concreta.

| Building block | ¿Se usa? | Para qué en este sistema |
|----------------|:--------:|--------------------------|
| Pub/Sub | ✅ | Eventos `ReservaConfirmada` y de catálogo; broker abstraído |
| State management | ✅ | Redis para idempotencia (inbox) y outbox |
| Secrets | ✅ | Key Vault (nube) / archivo (local) — sin credenciales en código |
| Resiliency | ✅ | Retry / circuit-breaker / timeout declarativos (YAML) |
| Observability | ✅ | Emite trazas OTel de cada salto → tracing distribuido |
| Service invocation | Opcional | Llamada servicio-a-servicio con mTLS (se prefieren eventos) |
| Bindings | Opcional | Alternativa para el envío de correo (SMTP/SendGrid) |
| Actors / Workflow | ❌ | Sin caso; Workflow serviría si la reserva creciera a una *saga* |

## Resiliencia (selectiva)

Vía **Polly / `Microsoft.Extensions.Http.Resilience`** (aplicado por defecto por Aspire). Solo donde el fallo externo es real:
- Envío de correo en `Notificaciones.Worker` (SMTP puede fallar) → retry + circuit breaker + timeout.
- Llamadas entre servicios vía Dapr (que además trae sus propios retries).

No se aplica indiscriminadamente en cada método (evitar sobre-ingeniería).
