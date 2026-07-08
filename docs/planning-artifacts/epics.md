---
stepsCompleted: [1, 2, 3]
inputDocuments:
  - docs/planning-artifacts/prds/prd-hotel-booking-hub-2026-07-08/prd.md
  - docs/planning-artifacts/architecture.md
project_name: 'hotel-booking-hub'
user_name: 'Santiago'
date: '2026-07-08'
---

# hotel-booking-hub - Epic Breakdown

## Overview

Este documento fragmenta los requisitos del [PRD](prds/prd-hotel-booking-hub-2026-07-08/prd.md) y las decisiones de la [arquitectura](architecture.md) en épicas e historias implementables, cada una con criterios de aceptación testables para el desarrollador. No hay documento de UX: la entrega es exclusivamente back end (non-goal explícito del PRD).

> **Autoridad de contrato:** ante cualquier discrepancia, la **arquitectura** (`architecture.md` + ADR) es autoritativa sobre el PRD. En particular, el anti-overbooking se arbitra con **índice `UNIQUE(HabitacionId, Noche)` + READ COMMITTED (ADR-016)**, que **reemplaza** la mención a `SERIALIZABLE` que aún queda en FR-18/NFR-2 del PRD (pendiente de sincronizar).

## Requirements Inventory

### Functional Requirements

**F1 — Gestión de hoteles e inventario (Agente) · CAP-1, CAP-2**

- **FR-1** — El agente crea un hotel con nombre, ciudad, dirección, descripción y estado (habilitado/deshabilitado).
- **FR-2** — El agente edita los datos de un hotel existente.
- **FR-3** — El agente elimina lógicamente un hotel (**soft delete**): se marca inactivo sin borrado físico; un hotel inactivo no aparece en búsquedas ni oferta habitaciones.
- **FR-4** — El agente habilita/deshabilita un hotel; el cambio se refleja de inmediato en la ofertabilidad.
- **FR-5** — El agente añade una habitación a un hotel con tipo, costo base, impuestos, ubicación y estado.
- **FR-6** — El agente edita una habitación de forma independiente del hotel (editar la habitación no altera el hotel).
- **FR-7** — El agente habilita/deshabilita una habitación individualmente; una habitación deshabilitada, o perteneciente a un hotel deshabilitado, no se oferta.

**F2 — Búsqueda y reserva (Viajero) · CAP-4, CAP-5**

- **FR-8** — El viajero busca habitaciones disponibles por ciudad, fecha de entrada, fecha de salida y cantidad de huéspedes. El resultado incluye solo habitaciones activas, con capacidad ≥ huéspedes y libres en todo el rango `[entrada, salida)`; las ya reservadas en ese rango no aparecen.
- **FR-9** — El viajero crea una reserva sobre una habitación disponible; se **crea y confirma en una sola operación** (sin borrador), quedando `Confirmada` de forma atómica con la reserva de los slots de inventario (ver FR-18).
- **FR-10** — La reserva registra los datos completos de **cada huésped** (nombres, apellidos, fecha de nacimiento, género, tipo y número de documento, email, teléfono); todos obligatorios y validados con FluentValidation; campos faltantes o inválidos → **400** (Problem Details).
- **FR-11** — La reserva registra un **contacto de emergencia** (nombre completo y teléfono).
- **FR-12** — El sistema calcula el precio total `= (costoBase + impuesto) × noches` y lo expone al confirmar.

**F3 — Reservas del agente y ciclo de vida (cancelación) · CAP-3, CAP-10, CAP-11**

- **FR-13** — El agente lista las reservas de **sus** hoteles y consulta el detalle completo de cada una; no ve reservas de otros agentes.
- **FR-14** — **Solicitud de cancelación.** El viajero solicita la cancelación de su propia reserva `Confirmada` con estancia no iniciada; el **agente puede iniciarla en su nombre**. Se registra el **motivo** (categoría + texto libre) y su origen (viajero/agente), la reserva pasa a `CancelacionSolicitada` y se **congela la penalidad sugerida**. Segunda solicitud sobre reserva con solicitud en curso → **409**.
- **FR-15** — **Política sugerida (default).** Con la fecha de solicitud como referencia: **≥30 días** → **0 %**; **<30 días** → **100 %**. Es sugerencia calculada, no imposición; sin cobro real (monto adeudado).
- **FR-16** — **Resolución por el agente (discreción, auditada).** Aprobar aplicando la penalidad sugerida, aprobar condonándola, o rechazar. Aprobar → `Cancelada`, **libera slots**, registra **penalidad decidida** (flag default/override) y quién decidió. Rechazar → vuelve a `Confirmada` con motivo, sin liberar slots. Segunda resolución concurrente → **409** (guard + `rowversion`); agente ajeno al hotel → **403**.
- **FR-17** — **Atajo, ciclo de vida y visibilidad.** El agente puede **solicitar y resolver en una sola operación** (viajero por teléfono), registrando ambos eventos. Ciclo `Confirmada → CancelacionSolicitada → {Cancelada | Confirmada}` con **guards**; solicitudes pendientes exponen su **antigüedad ("días en espera")**, sin expiración automática.

**F4 — Garantía anti-overbooking · CAP-6**

- **FR-18** — Nunca coexisten dos reservas activas para la misma habitación con estancias solapadas, aun bajo concurrencia. La garantía reside en el **motor de datos** (unicidad de slots `NochesHabitacion` + índice `UNIQUE(HabitacionId, Noche)` arbitrado en el INSERT bajo **READ COMMITTED**; ADR-016), nunca en lógica de aplicación; el intento perdedor recibe **409 Conflict** (Problem Details).

**F5 — Notificaciones · CAP-7 (+ CAP-10/CAP-11)**

- **FR-19** — Al confirmarse una reserva, notifica por correo al huésped y al agente, **sin pérdida** (Transactional Outbox) ni **duplicado** (idempotencia por message-id).
- **FR-20** — Al **solicitarse** una cancelación, avisa al agente (por resolver) y envía al viajero un **acuse con la penalidad estimada**, marcada como estimación (no cobro final).
- **FR-21** — Al **resolverse**, notifica al viajero (y al agente): aprobación con **penalidad final** (nota del agente si difiere), **condonación**, o **rechazo indicando que la reserva sigue `Confirmada`** y su motivo.

**F6 — Seguridad y acceso · CAP-8**

- **FR-22** — Toda operación exige autenticación (JWT/OIDC); petición sin token válido → **401**.
- **FR-23** — Autorización por rol (**Agente / Viajero**), resuelta server-side; rol sin permiso → **403**.
- **FR-24** — Aislamiento entre agentes: un agente no puede leer ni modificar hoteles o reservas de otro agente.

**F7 — Observabilidad · CAP-9**

- **FR-25** — Traza distribuida con `trace-id` propagado Gateway→servicio→sidecar Dapr→worker; ante un fallo, el span exacto es visible.
- **FR-26** — Métricas de duración **p95/p99** por endpoint para detectar degradación.

### NonFunctional Requirements

- **NFR-1 · Rendimiento y escalabilidad** — Búsqueda servida por proyección de lectura + caché Redis; p95/p99 estables bajo carga concurrente de escritura (G7). Escala objetivo ≈10.000 reservas/día; SQL Server en instancia única para escritura; microservicios escalables de forma independiente.
- **NFR-2 · Concurrencia y consistencia** — Invariante anti-overbooking garantizado en el motor (unicidad de slots + índice único / READ COMMITTED, ADR-016); concurrencia optimista con `rowversion` en agregados; consistencia eventual entre Bounded Contexts vía eventos + proyecciones.
- **NFR-3 · Fiabilidad de mensajería** — *At-least-once* (Transactional Outbox en la misma transacción que la reserva) + *effectively-once* (idempotencia por message-id en Redis) + DLQ/retries; sobrevive a caída del broker sin perder eventos.
- **NFR-4 · Seguridad** — JWT/OIDC + RBAC server-side; 8 prácticas mapeadas a OWASP Top 10 (2021); **cero secretos en el repositorio** (Dapr Secrets local / Key Vault nube); SAST + gitleaks en CI.
- **NFR-5 · Observabilidad** — OpenTelemetry (trazas, métricas, logs) con `trace-id` de extremo a extremo; dashboard de Aspire local / Application Insights nube.
- **NFR-6 · Portabilidad y despliegue** — `docker compose up` levanta el sistema completo **sin instalar SDK ni Aspire**; *cloud-agnostic* vía Dapr; despliegue a Azure **exclusivamente por Terraform (IaC)**, sin click-ops.
- **NFR-7 · Mantenibilidad y calidad** — Clean Architecture + DDD + CQRS (mediator propio); Result Pattern; Problem Details RFC 7807; `DateTimeOffset` (nunca `DateTime`); UUID v7 en PKs (no-clustered + clustering key `Seq`, ADR-017); versionado por URL `/api/v1/`. Cobertura ≥80% en código nuevo; TDD (Red→Green→Refactor) en el flujo crítico.
- **NFR-8 · Contratos de API** — Cada microservicio expone REST documentado con OpenAPI; UI vía Scalar; contratos versionados.

### Additional Requirements

_Derivados de la arquitectura; impactan la secuencia y las primeras historias._

- **[ARQ-1] Sin starter template (greenfield a medida, ADR-015)** — el esqueleto se construye a mano: `dotnet new sln` + `aspire-apphost` + `aspire-servicedefaults` + servicios de dominio; **Central Package Management** (`Directory.Packages.props`) y `Directory.Build.props` (net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors + CA2016) **desde el primer commit**. → **Historia 1.1 (inicialización)**.
- **[ARQ-2] Dos spikes de validación de ejecución (Sprint 0)** — (a) *money test* G1×G3×proyección (50-100 concurrentes + broker caído + fault-injection de deadlock 1205 → 1 confirmada, resto 409, todo-o-nada, exactamente un evento de outbox, 0 pérdida/duplicado); (b) wiring del mediator (ADR-018: pipeline compone en orden + outbox y dominio comparten `SaveChanges`). **Criterio de aborto/Plan B:** caer a `SERIALIZABLE` sin retry si no cierra en el timebox.
- **[ARQ-3] Infra reproducible** — SQL Server ×2 (una BD por servicio), Redis, RabbitMQ/Dapr pub/sub; `docker-compose.yml` **a mano** (ADR-007) + smoke test `/health` en CI (G2); AppHost de Aspire declara recursos + sidecars Dapr para el dev-loop.
- **[ARQ-4] Concurrencia y claves** — INSERT de slots bajo READ COMMITTED; clasificación por `SqlException.Number`: **2627/2601 → 409 sin retry**, **1205 → retry 3× backoff+jitter** en `TransactionBehavior`; slots insertados en orden determinístico; liberación al aprobar = DELETE físico. Claves: UUID v7 no-clustered + `Seq bigint IDENTITY` (shadow property, nunca cruza el BC); FK→Guid; `NochesHabitacion` clustered por `(HabitacionId, Noche)`.
- **[ARQ-5] Mensajería y proyección** — Outbox manual (`OutboxMessages` en la misma tx; `MessageId` asignado una vez antes del retry; `UNIQUE(MessageId)`) + relay `BackgroundService` con lease-expiry/re-claim; **idempotencia del consumidor** por (`MessageId`, `version`) vía Redis SETNX+TTL; **proyección `ProyeccionHabitacion` idempotente y ordenada** + job de reconciliación; contrato de eventos con envelope `{id,type,version,occurredAt,traceId,data}` + semver en `type` + compatibilidad hacia atrás.
- **[ARQ-6] Mediator propio (ADR-005/018)** — `IRequestHandler<TRequest,TResponse>` con `TResponse = Result/Result<T>`; pipeline por decorators `Logging → Validation → Transaction → Outbox → Handler`; registro por scan de assembly en un único `AddMediatorPipeline()`; `TransactionBehavior` solo en comandos. Requiere test de pipeline.
- **[ARQ-7] Gateway y bordes** — API Gateway YARP (`dotnet new web` + `Yarp.ReverseProxy`): enruta/agrega, auth JWT, rate limiting, HTTPS; los servicios no se exponen directamente.
- **[ARQ-8] Enforcement y estructura** — 4 assemblies por BC (`.Domain/.Application/.Infrastructure/.Api`) + slices en Application; `Comun` solo contratos transversales (regla de admisión); **NetArchTest** para disciplina de capas; `.editorconfig` + analyzers + `TreatWarningsAsErrors`; **check de CI sobre la lista cerrada de sufijos** leída de `AGENTS.md`.
- **[ARQ-9] Estrategia de tests** — `*.UnitTests` (xUnit + InMemory/puro + NetArchTest), `*.IntegrationTests` (Testcontainers.MsSql), `Contracts` (esquema de eventos), `E2E` (compose); `TestKit` centraliza fixtures (imagen pineada), builders y collections; **G1 y OutboxFaultInjection en collections aisladas `DisableParallelization=true`** con fake controlable de Dapr; en CI corren como stage secuencial aparte.
- **[ARQ-10] CI/CD** — GitHub Actions: build + unit + integration (Testcontainers.MsSql) + contracts + `dotnet format` + gitleaks/SAST + Newman + smoke test de compose + check de sufijos.
- **[ARQ-11] Entregables de documentación derivados** — `AGENTS.md` (reglas canónicas), `README` (C4 de contenedores + árbol comentado + enrutador), ADR-015/016/017/018 en `docs/adr/`, diagrama de secuencia de la escritura crítica (reserva + slots + outbox en una tx) en `architecture-diagrams.md`, colección Postman/Newman, doc de uso de IA.

### UX Design Requirements

No aplica — la entrega es exclusivamente back end (non-goal del PRD). No existe documento de UX.

### FR Coverage Map

- **FR-1** — E2 · crear hotel (nombre, ciudad, dirección, descripción, estado)
- **FR-2** — E2 · editar hotel (independiente de la habitación)
- **FR-3** — E2 · soft delete de hotel
- **FR-4** — E2 · habilitar/deshabilitar hotel → ofertabilidad inmediata
- **FR-5** — E2 · añadir habitación (tipo, costo, impuestos, ubicación, estado)
- **FR-6** — E2 · editar habitación (independiente del hotel)
- **FR-7** — E2 · habilitar/deshabilitar habitación
- **FR-8** — E3 · búsqueda de disponibilidad (ciudad/fechas/huéspedes) sobre proyección + Redis
- **FR-9** — E1 · crear-confirmar reserva atómica
- **FR-10** — E1 · datos completos de cada huésped (validados)
- **FR-11** — E1 · contacto de emergencia
- **FR-12** — E1 · cálculo de precio `(costoBase + impuesto) × noches`
- **FR-13** — E3 · listado de reservas del agente + detalle
- **FR-14** — E4 · solicitud de cancelación (dos vías) + penalidad congelada
- **FR-15** — E4 · política sugerida (≥30d 0% / <30d 100%)
- **FR-16** — E4 · resolución del agente (aprobar/condonar/rechazar) + auditoría
- **FR-17** — E4 · atajo de un paso + ciclo de vida con guards + "días en espera"
- **FR-18** — E1 · anti-overbooking en el motor (índice único + READ COMMITTED, ADR-016)
- **FR-19** — E5 · notificación de reserva confirmada (huésped + agente)
- **FR-20** — E5 · notificación de solicitud de cancelación (acuse con penalidad estimada)
- **FR-21** — E5 · notificación de resolución (penalidad final / condonación / rechazo)
- **FR-22** — E6 · autenticación JWT/OIDC (401)
- **FR-23** — E6 · autorización por rol Agente/Viajero (403)
- **FR-24** — E6 · aislamiento entre agentes
- **FR-25** — E7 · traza distribuida con trace-id propagado extremo a extremo
- **FR-26** — E7 · métricas p95/p99 por endpoint
- **NFR-6** — E8 · despliegue reproducible a Azure por Terraform (IaC)

> **Regla de propiedad de eventos (party-mode, Winston).** Cada épica que **produce** un evento lo **define y lo prueba** en su propia entrega, aunque el consumidor no exista todavía: E1 el evento de reserva, E2 los eventos de catálogo, E4 los eventos de cancelación. Así ningún consumidor (E5/E3) obliga a reabrir un productor.

## Epic List

### Épica 1: Fundación ejecutable y anti-overbooking probado
🎯 *Núcleo intocable · frontera de riesgo · Fase 0→1.* Al cerrar esta épica, el sistema **arranca con un comando** (`docker compose up` → `/health`, G2) y se puede **crear una reserva que jamás sobrevende bajo concurrencia**, con el invariante garantizado por el motor de datos. Front-carga los dos spikes de Sprint 0 para retirar el riesgo de ejecución antes de construir nada encima.

**Estructura en dos gates legibles (party-mode, Winston):**
- **E1a — Esqueleto + spikes (gate de riesgo).** Solución + CPM + `Directory.Build.props` + Aspire (AppHost/ServiceDefaults) + estructura de 4 assemblies × BC + wiring del mediator + health + CI verde. Incluye los **spikes timeboxed y desechables** (fuera de historia de entrega): (a) arbitraje `2627`/`1205` sobre Testcontainers.MsSql, (b) composición del pipeline del mediator (ADR-018). **Plan B:** si E1a no cierra en su timebox, se cae a `SERIALIZABLE` sin retry (revierte parcialmente ADR-016, ya trazado).
- **E1b — Anti-overbooking productivo.** `CalculadorPrecio` (TDD puro) → `NochesHabitacion` + índice único + arbitraje 2627/1205 → `CrearReserva` (huésped + contacto de emergencia + precio) + escritura del outbox en la misma transacción.

**Alcance de verificación — honesto (party-mode, Murat).** El criterio de esta épica es **"invariante anti-overbooking + atomicidad y resiliencia del *productor* del outbox"**, NO la garantía end-to-end exactly-once.
- **`AC-E1` (cierra aquí, GREEN de verdad):** exactamente 1 reserva confirmada + resto 409 bajo N concurrentes; rollback **todo-o-nada a nivel reserva** ante deadlock 1205 inyectado; atomicidad del outbox (**1 fila / 0 huérfanos**, verificado inspeccionando la tabla con Testcontainers.MsSql); resiliencia del relay con broker caído (**0 pérdida**, sin "mark sent" prematuro, `deliveries >= 1` — nunca `== 1`). G1 y OutboxFaultInjection en collections xUnit aisladas (`DisableParallelization=true`).
- **`AC-E3/E5` (deuda de verificación, visible aquí, cierra después):** idempotencia del consumidor → efecto exactamente-una-vez (E5); convergencia y orden de la proyección (E3). *"Muéstrame quién deduplica": sin consumidor no hay garantía end-to-end.*
- **Congelar el contrato del evento aquí (party-mode, Murat/Winston):** el evento de reserva se emite en su **forma final versionada** (envelope `{id,type,version,occurredAt,traceId,data}` + **dedup key** estable + **order key**), fijado con un test de `Contracts`. Si E1 no emite el dedup/order key, E5 no *puede* deduplicar ni E3 ordenar.

**FRs cubiertos:** FR-9, FR-10, FR-11, FR-12, FR-18.

### Épica 2: Gestión de hoteles e inventario (Agente)
*Núcleo · Fase 1.* El agente administra su catálogo de punta a punta: crea/edita hoteles, elimina lógicamente (soft delete), añade/edita habitaciones de forma independiente y habilita/deshabilita hotel y habitación con reflejo inmediato en la ofertabilidad. **Define y prueba los eventos de catálogo** (`HabitacionAgregada`, `PrecioHabitacionCambiado`, `HabitacionDeshabilitada`, etc.) que alimentarán la proyección de disponibilidad de E3 — el productor fija el contrato.

**FRs cubiertos:** FR-1, FR-2, FR-3, FR-4, FR-5, FR-6, FR-7.

### Épica 3: Búsqueda de disponibilidad y reservas del agente
*Núcleo · Fase 1.* Cierra el lado de **lectura/consulta** (CQRS): el viajero busca habitaciones **realmente disponibles** (proyección `ProyeccionHabitacion` alimentada por los eventos de E2 + caché Redis), y el agente consulta las reservas de **sus** hoteles con el detalle completo. Aquí vive el **consumidor idempotente y ordenado** de la proyección + el job de reconciliación, y aquí cierra la parte `AC-E3` (convergencia/orden) que E1 dejó como deuda.

> **Frontera E1/E3 (party-mode, John).** E1 = *el viajero crea y confirma* (camino de **escritura**, dueño del invariante anti-overbooking). E3 = *búsqueda + el agente consulta sus reservas* (camino de **lectura/consulta**). No hay duplicación: `CrearReserva` vive solo en E1; E3 abre slices nuevos (`BuscarDisponibilidad`, `ObtenerReservasAgente`).

**FRs cubiertos:** FR-8, FR-13.

### Épica 4: Ciclo de vida de la reserva — cancelación con discreción
*Diferenciador (profundo) · valor más allá del enunciado · Fase 1 (núcleo de dominio).* Viajero o agente solicitan la cancelación (política de penalidad **sugerida y congelada** en la fecha de solicitud); el agente **resuelve con criterio** —aprobar aplicando, condonar o rechazar— con auditoría de quién inició y quién decidió; al aprobar se **libera el inventario** (DELETE de slots). Incluye el atajo de un paso (viajero por teléfono) y "días en espera" sin expiración automática. **Define y prueba sus eventos de cancelación** aunque E5 aún no los consuma.

**FRs cubiertos:** FR-14, FR-15, FR-16, FR-17.

### Épica 5: Notificaciones por correo sin pérdida ni duplicado
*Diferenciador (profundo) · contiene un criterio OBLIGATORIO del enunciado (HU2-5) · Fase 2.* Huésped y agente reciben correos de **confirmación de reserva y de cancelación**, sin pérdida (outbox de E1/E4) ni duplicado, sobreviviendo a la caída del broker (G3). Aquí viven `Notificaciones.Worker` + SMTP + la **idempotencia del consumidor** (Redis SETNX+TTL) que cierra la parte `AC-E5` (efecto exactamente-una-vez) que E1 dejó como deuda; reutiliza la colección `OutboxFaultInjection` de E1 encendiendo el assert de "0 efecto duplicado".

> **No recortable.** FR-19 (correo de confirmación) satisface **HU2-5**, criterio obligatorio de la prueba. A diferencia de E7/E8, esta épica no se recorta.

**FRs cubiertos:** FR-19, FR-20, FR-21.

### Épica 6: Seguridad y acceso
*Núcleo (acceso) + diferenciador (OWASP) · Fase 1→2.* Toda operación exige autenticación (401), autorización por rol Agente/Viajero resuelta server-side (403) y **aislamiento entre agentes** (un agente no ve ni toca lo de otro). El acceso 401/403/aislamiento es núcleo de Fase 1; el mapeo completo de las 8 prácticas a OWASP Top 10 y cero secretos en el repo se profundiza en Fase 2.

**FRs cubiertos:** FR-22, FR-23, FR-24.

### Épica 7: Observabilidad de extremo a extremo
*Diferenciador · recortable · Fase 2.* `trace-id` propagado Gateway→servicio→sidecar Dapr→worker (el span exacto visible ante un fallo) + métricas **p95/p99** por endpoint. La base OTel llega del ServiceDefaults (E1); esta épica cierra la propagación cruzada por Dapr, la correlación trace-id de negocio vs técnico, y los dashboards de degradación.

**FRs cubiertos:** FR-25, FR-26.

### Épica 8: Nube por IaC (con compuerta)
*Recortable — primero en recortarse.* Despliegue reproducible a Azure **exclusivamente por Terraform**: ACA + Azure SQL + Cache for Redis + Service Bus + Key Vault + App Insights. Queda como IaC documentada si el tiempo aprieta; no bloquea ningún criterio obligatorio.

**Cubre:** NFR-6 (portabilidad y despliegue).

---

## Convención de historias y criterios de aceptación

_Disciplina fija (derivada de party-mode: Mary, Paige, Murat). Aplica a todas las historias de este documento._

- **Encabezado de trazabilidad** en cada historia: `HU-x → FR-n → AC-id`, etiqueta **Obligatorio** (criterio del enunciado) o **Diferenciador** (valor añadido), y un bloque **Porqué** de 1-2 frases que ata la decisión al criterio de evaluación ("claridad del razonamiento").
- **Un `Dado/Cuando/Entonces` = una aserción observable.** ID estable `AC-Ex.n`. **Números, no adjetivos** (`exactamente 1`, `409`, `deliveries >= 1`). Ramas múltiples → tabla de ejemplos.
- **Tipografía tri-idioma:** identificador de dominio/estado en `código` (español sin tilde, p. ej. `Habitacion`, estado `Confirmada`); mensaje de negocio entre "comillas" (español con tilde, p. ej. `"La habitación ya está reservada"`); sufijo de patrón en inglés (`CrearReservaCommand`).
- **AC negativos** para los obligatorios frágiles (campo ausente → rechazo; falso 409; round-trip de liberación).
- **Deuda de verificación** rastreable: `[DEUDA-VERIF:Ex]` en el AC que no cierra en su épica, nombrando la épica que lo salda. En E1: `deliveries >= 1` **nunca** `== 1`.

---

## Epic 1: Fundación ejecutable y anti-overbooking probado

🎯 *Núcleo intocable · frontera de riesgo · Fase 0→1.* Al cerrar la épica, el sistema arranca con un comando y se puede crear una reserva que jamás sobrevende bajo concurrencia, con el invariante garantizado por el motor de datos. Gates: **E1a** (esqueleto + spikes + contrato de evento) · **E1b** (anti-overbooking productivo). Alcance de verificación: `AC-E1` cierra aquí (productor); `AC-E3/E5` es deuda diferida.

### Story 1.1: Esqueleto ejecutable de un comando (walking skeleton)

> **Trazabilidad:** G2 · NFR-6 · NFR-7 → *(historia habilitadora, sin FR de negocio)* → `AC-E1.1.x` · **Obligatorio (infraestructura de entrega)**
> **Porqué:** el enunciado exige `docker-compose` funcional y evaluación sin instalar SDK; un esqueleto que arranca de un comando retira el riesgo G2 primero y es la topología de archivos que toda historia posterior extiende (ADR-015: sin `aspire-starter`).

Como **evaluador de la prueba**,
quiero **levantar todo el sistema con un solo comando y verificar que responde**,
para **revisar la solución sin instalar el SDK de .NET ni los workloads de Aspire**.

**Acceptance Criteria:**

**AC-E1.1.1 — Arranque reproducible**
**Dado** un checkout limpio del repositorio sin SDK de .NET instalado
**Cuando** ejecuto `docker compose up`
**Entonces** los servicios (`ApiGateway`, `Hoteles.Api`, `Reservas.Api`, `Notificaciones.Worker`) alcanzan estado *healthy*
**Y** `GET /health` responde `200` en cada servicio.

**AC-E1.1.2 — Estructura y gobernanza desde el primer commit**
**Dado** el repositorio inicializado
**Cuando** inspecciono la solución
**Entonces** existen 4 assemblies por BC (`.Domain`, `.Application`, `.Infrastructure`, `.Api`), `Directory.Packages.props` (CPM) y `Directory.Build.props` (`net10.0`, `Nullable`, `TreatWarningsAsErrors` con `CA2016`)
**Y** `NetArchTest` verifica que `Reservas.Domain` no referencia `Microsoft.EntityFrameworkCore`.

**AC-E1.1.3 — CI verde y smoke test de compose**
**Dado** un push a `develop`
**Cuando** corre el pipeline de CI
**Entonces** `build` + `dotnet format` + `gitleaks` pasan
**Y** el smoke test (`docker compose up` + verificación de `/health`) pasa, detectando *drift* del compose a mano (ADR-007).

### Story 1.2: Spikes de validación de ejecución (Sprint 0, timeboxed)

> **Trazabilidad:** riesgo de ejecución (G1, ADR-016/018) → *(spike de reducción de incertidumbre — código desechable, sin cobertura, NO cuenta como entregable productivo)* → `AC-E1.2.x` · **Obligatorio (gate de riesgo)**
> **Porqué:** el `architecture.md` declara "diseño completo, ejecución NO validada"; estos spikes retiran ese riesgo antes de construir el core. Salen del código de entrega precisamente para no contaminar la regla de "100% verde" con código throwaway.

Como **equipo de desarrollo**,
quiero **validar en un timebox que el arbitraje por índice único y el wiring del mediator funcionan sobre infraestructura real**,
para **confirmar el diseño (o disparar el Plan B) antes de invertir en el core**.

**Acceptance Criteria:**

**AC-E1.2.1 — Spike de arbitraje de concurrencia (go/no-go)**
**Dado** un `NochesHabitacion` con `UNIQUE(HabitacionId, Noche)` sobre Testcontainers.MsSql
**Cuando** dos INSERT concurrentes compiten por la misma noche bajo `READ COMMITTED`
**Entonces** uno commitea y el otro recibe `SqlException.Number` `2627`/`2601` (clasificado, sin parsear el mensaje)
**Y** un `1205` (deadlock) se distingue como reintentable
**Y** *(criterio de aborto)* si no se logra la garantía en el timebox, se documenta y se dispara el **Plan B**: `SERIALIZABLE` sin retry (revierte parcialmente ADR-016, ya trazado).

**AC-E1.2.2 — Spike del pipeline del mediator (go/no-go)**
**Dado** un `IRequestHandler<TRequest, TResponse>` con `TResponse = Result`
**Cuando** se ejecuta un comando trivial a través del pipeline `Logging → Validation → Transaction → Outbox → Handler`
**Entonces** los behaviors se componen en ese orden literal
**Y** el insert de dominio y el de `OutboxMessages` comparten el mismo `SaveChangesAsync` (ADR-018).

### Story 1.3: Contrato del evento `ReservaConfirmada` (claves de dedup y orden congeladas)

> **Trazabilidad:** NFR-3 · NFR-8 → *(habilitadora de contrato; base de E3 y E5)* → `AC-E1.3.x` · **Obligatorio (contrato)**
> **Porqué:** E5 (idempotencia) y E3 (orden de proyección) dependen de estas claves para existir; congelarlas ahora evita reabrir el productor después (party-mode: Winston/Murat). Va en E1a, no detrás de lógica de negocio.

Como **consumidor de eventos (Worker / proyección)**,
quiero **un contrato de evento versionado con clave de deduplicación y clave de orden estables**,
para **poder deduplicar y ordenar sin acoplarme a la implementación del productor**.

**Acceptance Criteria:**

**AC-E1.3.1 — Congelamiento del esquema (contract test)**
**Dado** el esquema publicado de `ReservaConfirmada.v1` (envelope `{ id, type, version, occurredAt, traceId, data }`)
**Cuando** valido un evento serializado contra el snapshot del contrato
**Entonces** existe y es no-nula la **dedup key** (`id`/`MessageId`)
**Y** existe y es no-nula la **order key** (`{ aggregateId, version }`)
**Y** un cambio incompatible en esas claves **rompe** el test (snapshot/JSON Schema).
**Y** el test documenta: dedup key → la consume E5; order key → la consume E3.

### Story 1.4: Cálculo de precio de la reserva (`CalculadorPrecio`)

> **Trazabilidad:** HU2 → **FR-12** → `AC-E1.4.x` · **Obligatorio**
> **Porqué:** es dominio puro sin I/O, 100% unit-testeable → sostiene el TDD del flujo crítico (Red→Green→Refactor evidenciado en commits) que el enunciado valora.

Como **viajero**,
quiero **ver el precio total de la reserva calculado de forma correcta**,
para **saber cuánto pagaré antes de confirmar**.

**Acceptance Criteria:**

**AC-E1.4.1 — Fórmula de precio**
**Dado** un `costoBase`, un `impuesto` y una `Estancia` de N noches
**Cuando** se invoca `CalculadorPrecio`
**Entonces** el total es `(costoBase + impuesto) × N` usando `decimal` (nunca `double`).

| costoBase | impuesto | noches | total |
|---|---|---|---|
| 100.00 | 19.00 | 3 | 357.00 |
| 80.00 | 0.00 | 1 | 80.00 |

**AC-E1.4.2 — Estancia inválida**
**Dado** una `Estancia` con `salida <= entrada`
**Cuando** se construye el value object
**Entonces** se rechaza con el mensaje `"La fecha de salida debe ser posterior a la de entrada"` (no se calcula precio).

### Story 1.5: Anti-overbooking — slots `NochesHabitacion` + índice único + arbitraje

> **Trazabilidad:** HU2 → **FR-18** → `AC-E1.5.x` · **Obligatorio** · *núcleo del diseño*
> **Porqué:** el invariante ("cero overbooking") es la promesa central del sistema; vive en el motor de datos (ADR-016), no en la aplicación. Es el corazón del reto de nivel senior.

Como **operador del sistema**,
quiero **que dos reservas solapadas de la misma habitación sean imposibles a nivel de motor**,
para **garantizar cero overbooking aun bajo concurrencia**.

**Acceptance Criteria:**

**AC-E1.5.1 — El índice único arbitra el conflicto (persistencia)**
**Dado** un slot libre `(HabitacionId, Noche)` sobre Testcontainers.MsSql
**Cuando** dos inserciones concurrentes compiten por esa misma noche bajo `READ COMMITTED`
**Entonces** `exactamente 1` commitea
**Y** la otra recibe violación de único (`SqlException.Number` `2627`/`2601`) → se traduce a `409` sin retry.

**AC-E1.5.2 — Retry acotado solo para deadlock**
**Dado** una inserción multi-noche que sufre un deadlock (`1205`)
**Cuando** el `TransactionBehavior` la reejecuta
**Entonces** reintenta hasta 3 veces con backoff + jitter
**Y** los slots se insertan en orden determinístico `ORDER BY HabitacionId, Noche` para minimizar deadlocks.

**AC-E1.5.3 — Falso 409 (AC negativo — el riesgo silencioso)**
**Dado** dos reservas en la **misma** habitación con estancias **adyacentes no solapadas** `[D1→D2]` y `[D2→D3]` (check-out == check-in, **no** es solape)
**Cuando** se solicitan (incluso concurrentes)
**Entonces** `ambas` confirman y `conflicts == 0`.
**Y** dado dos reservas en habitaciones **distintas** con las mismas fechas → `ambas` confirman (el constraint no cruza habitaciones).

### Story 1.6: Crear-confirmar reserva atómica (huésped + contacto de emergencia + outbox)

> **Trazabilidad:** HU2-2/3/4 → **FR-9, FR-10, FR-11** → `AC-E1.6.x` · **Obligatorio**
> **Porqué:** es la operación de negocio del viajero y el flujo crítico TDD; reúne precio (1.4), slots (1.5) y la escritura del outbox en una sola transacción. Aquí cierra `AC-E1` (productor) y se deja escrita la deuda `AC-E3/E5`.

Como **viajero**,
quiero **reservar una habitación disponible registrando mis datos y un contacto de emergencia, y confirmarla en una sola operación**,
para **obtener alojamiento con confirmación inmediata**.

**Acceptance Criteria:**

**AC-E1.6.1 — Money test: confirmación única bajo concurrencia**
**Dado** una `Habitacion` con un único slot libre para la estancia `[D]`
**Cuando** se ejecutan N solicitudes `CrearReservaCommand` concurrentes sobre ese slot
**Entonces** se cumple exactamente:

| N | confirmadas (`201`) | rechazadas (`409`) | filas `Reserva` `Confirmada` | filas `Outbox` |
|---|---|---|---|---|
| 2 | 1 | 1 | 1 | 1 |
| 50 | 1 | 49 | 1 | 1 |

**Y** no existe fila en `OutboxMessages` para ninguna de las reservas rechazadas.
*(Collection `G1` aislada, `DisableParallelization = true`.)*

**AC-E1.6.2 — Atomicidad del outbox (misma transacción)**
**Dado** un fallo inyectado entre el insert de la `Reserva` y el de `OutboxMessages`
**Cuando** la transacción se resuelve
**Entonces** `count(reservas Confirmada) == 0` **Y** `count(filas Outbox) == 0` (las dos o ninguna).
*(Collection `OutboxFaultInjection` aislada, `DisableParallelization = true`.)*

**AC-E1.6.3 — Resiliencia del relay (at-least-once del productor)**
**Dado** una reserva confirmada con su fila de outbox y el broker caído (fake controlable de Dapr)
**Cuando** el broker se recupera y el relay corre
**Entonces** la fila pendiente se publica con `deliveries >= 1` (**nunca** se asume `== 1`); sin "mark sent" prematuro.
**Y** `[DEUDA-VERIF:E5]` el colapso a un solo **efecto** (idempotencia del consumidor) se verifica en E5.
**Y** `[DEUDA-VERIF:E3]` el orden/convergencia de la proyección se verifica en E3.

**AC-E1.6.4 — Datos de cada huésped obligatorios (AC negativo)**
**Dado** un `CrearReservaCommand` con un huésped al que le falta un campo (nombres, apellidos, fecha de nacimiento, género, tipo/número de documento, email o teléfono) o con formato inválido
**Cuando** se valida (`ValidationBehavior` + FluentValidation)
**Entonces** responde `400` con Problem Details (RFC 7807) enumerando los campos inválidos; no se crea reserva.

**AC-E1.6.5 — Contacto de emergencia obligatorio (AC negativo)**
**Dado** un `CrearReservaCommand` sin `ContactoEmergencia` (nombre completo + teléfono)
**Cuando** se valida
**Entonces** responde `400` con Problem Details; no se crea reserva.

**AC-E1.6.6 — Confirmación exitosa expone el precio**
**Dado** un comando válido sobre una habitación disponible
**Cuando** se confirma
**Entonces** responde `201` con la `Reserva` en estado `Confirmada`, el precio total (AC-E1.4.1) y su identificador (UUID v7).

---

## Epic 2: Gestión de hoteles e inventario (Agente)

*Núcleo · Fase 1.* El agente administra su catálogo de punta a punta y emite los eventos que alimentarán la disponibilidad de E3 (el productor define y prueba sus eventos).

### Story 2.1: Crear hotel

> **Trazabilidad:** HU1-1 → **FR-1** → `AC-E2.1.x` · **Obligatorio**
> **Porqué:** primer eslabón del catálogo del agente; sin hotel no hay inventario que ofertar.

Como **agente de viajes**,
quiero **registrar un hotel con sus datos y estado**,
para **incorporarlo a mi catálogo y maximizar comisiones**.

**Acceptance Criteria:**

**AC-E2.1.1 — Alta válida**
**Dado** un `CrearHotelCommand` con nombre, ciudad, dirección, descripción y estado
**Cuando** se procesa
**Entonces** responde `201` con el `Hotel` creado (UUID v7) en el estado indicado.

**AC-E2.1.2 — Validación de campos (AC negativo)**
**Dado** un comando con nombre o ciudad vacíos
**Cuando** se valida
**Entonces** responde `400` con Problem Details enumerando los campos inválidos.

### Story 2.2: Editar hotel y eliminarlo lógicamente (soft delete)

> **Trazabilidad:** HU1-1/HU1-3 → **FR-2, FR-3** → `AC-E2.2.x` · **Obligatorio**
> **Porqué:** la edición independiente y el soft delete (nunca borrado físico) preservan trazabilidad e historial de comisiones.

Como **agente**,
quiero **editar los datos de un hotel y darlo de baja lógicamente**,
para **mantener el catálogo al día sin perder historial**.

**Acceptance Criteria:**

**AC-E2.2.1 — Edición independiente**
**Dado** un hotel existente
**Cuando** edito sus datos con `EditarHotelCommand`
**Entonces** responde `200` con los datos actualizados; las habitaciones del hotel no se alteran.

**AC-E2.2.2 — Soft delete**
**Dado** un hotel activo
**Cuando** lo elimino
**Entonces** queda marcado inactivo (sin borrado físico) y deja de aparecer en búsquedas y de ofertar habitaciones.

**AC-E2.2.3 — Edición concurrente (concurrencia optimista)**
**Dado** dos agentes que editan el mismo hotel con el mismo `rowVersion`
**Cuando** ambos guardan
**Entonces** `exactamente 1` confirma; el otro recibe `409` con instrucción de recargar.

### Story 2.3: Habilitar / deshabilitar hotel

> **Trazabilidad:** HU1-4 → **FR-4** → `AC-E2.3.x` · **Obligatorio**
> **Porqué:** el estado de publicación debe reflejarse de inmediato en la ofertabilidad.

Como **agente**,
quiero **habilitar o deshabilitar un hotel**,
para **controlar al instante si se oferta**.

**Acceptance Criteria:**

**AC-E2.3.1 — Reflejo inmediato**
**Dado** un hotel habilitado con habitaciones
**Cuando** lo deshabilito
**Entonces** ni el hotel ni sus habitaciones aparecen en búsquedas posteriores (vía evento a la proyección de E3).

### Story 2.4: Gestionar habitaciones del hotel

> **Trazabilidad:** HU1-2/HU1-3/HU1-4 → **FR-5, FR-6, FR-7** → `AC-E2.4.x` · **Obligatorio**
> **Porqué:** la habitación es la unidad reservable; su estado y datos se gestionan de forma independiente del hotel.

Como **agente**,
quiero **añadir, editar y habilitar/deshabilitar habitaciones de un hotel**,
para **gestionar el inventario ofertable con precisión**.

**Acceptance Criteria:**

**AC-E2.4.1 — Añadir habitación**
**Dado** un hotel existente
**Cuando** añado una habitación con tipo, costo base, impuestos, ubicación y estado
**Entonces** responde `201` con la `Habitacion` creada.

**AC-E2.4.2 — Edición independiente**
**Dado** una habitación existente
**Cuando** la edito
**Entonces** cambian solo sus datos; el `Hotel` no se altera.

**AC-E2.4.3 — Ofertabilidad compuesta**
**Dado** una habitación deshabilitada, o perteneciente a un hotel deshabilitado
**Cuando** se ejecuta una búsqueda
**Entonces** esa habitación no se oferta.

### Story 2.5: Emisión del contrato de eventos de catálogo

> **Trazabilidad:** NFR-3 · NFR-8 → *(productor define su evento; base de la proyección de E3)* → `AC-E2.5.x` · **Obligatorio (contrato)**
> **Porqué:** regla de propiedad de eventos — E2 (productor) fija y prueba `HabitacionAgregada`/`PrecioHabitacionCambiado`/`HabitacionDeshabilitada` aunque E3 aún no los consuma, para no reabrir el productor después.

Como **BC de Reservas (consumidor)**,
quiero **eventos de catálogo versionados y estables**,
para **construir la proyección de disponibilidad sin acoplarme a Hoteles**.

**Acceptance Criteria:**

**AC-E2.5.1 — Contrato de eventos de catálogo (contract test)**
**Dado** los eventos de catálogo publicados por `Hoteles`
**Cuando** valido su serialización contra el snapshot
**Entonces** cada uno lleva envelope versionado (`type` con semver) + order key `{ aggregateId, version }`
**Y** un cambio incompatible rompe el test.

**AC-E2.5.2 — Emisión transaccional**
**Dado** un cambio de catálogo (alta/edición de precio/deshabilitación)
**Cuando** se persiste
**Entonces** el evento se escribe en el outbox de `Hoteles` en la misma transacción (at-least-once).

---

## Epic 3: Búsqueda de disponibilidad y reservas del agente

*Núcleo · Fase 1.* Cierra el lado de lectura/consulta (CQRS). Aquí se salda la deuda `[DEUDA-VERIF:E3]` que dejó E1.

### Story 3.1: Proyección de habitaciones idempotente y ordenada

> **Trazabilidad:** NFR-1/NFR-2 · **[SALDA: AC-E1.6.3 (E3)]** → `AC-E3.1.x` · **Obligatorio (mecanismo)**
> **Porqué:** la búsqueda se sirve de una proyección alimentada por eventos de E2; debe converger aun con reentrega y desorden de Dapr, o mostraría inventario falso.

Como **BC de Reservas**,
quiero **mantener una `ProyeccionHabitacion` que converge bajo reentrega y desorden**,
para **que la búsqueda no mienta sobre la disponibilidad**.

**Acceptance Criteria:**

**AC-E3.1.1 — Convergencia bajo desorden**
**Dado** dos eventos del mismo agregado entregados fuera de orden (`v2` antes que `v1`)
**Cuando** el proyector los procesa
**Entonces** el estado final proyectado `== v2` (versión más alta)
**Y** reprocesar el `v1` tardío no retrocede el estado (order key respetada).

**AC-E3.1.2 — Idempotencia (sin duplicados)**
**Dado** el mismo evento entregado N veces
**Cuando** el proyector lo procesa
**Entonces** `filas_duplicadas == 0` en la proyección.

**AC-E3.1.3 — Reconciliación**
**Dado** una proyección corrupta o rezagada
**Cuando** corre el job de reconciliación/rebuild desde el event-log
**Entonces** la proyección converge al estado correcto.

### Story 3.2: Búsqueda de habitaciones disponibles

> **Trazabilidad:** HU2-1 → **FR-8** → `AC-E3.2.x` · **Obligatorio**
> **Porqué:** es la puerta de entrada del viajero; debe devolver solo lo realmente reservable, referenciando el invariante de E1 (no asumirlo).

Como **viajero**,
quiero **buscar habitaciones por ciudad, fechas y número de huéspedes**,
para **encontrar solo opciones realmente disponibles**.

**Acceptance Criteria:**

**AC-E3.2.1 — Filtro de disponibilidad real**
**Dado** habitaciones en una ciudad
**Cuando** busco por ciudad, `[entrada, salida)` y huéspedes
**Entonces** el resultado incluye solo habitaciones activas, con capacidad `>=` huéspedes y con todas las noches libres en el rango.

**AC-E3.2.2 — No mostrar lo no disponible (AC negativo)**
**Dado** una habitación ya reservada en `[entrada, salida)`, o deshabilitada, o de hotel deshabilitado
**Cuando** busco ese rango
**Entonces** esa habitación **no** aparece en los resultados.

**AC-E3.2.3 — Caché de lectura**
**Dado** una búsqueda repetida
**Cuando** el resultado está en caché Redis vigente
**Entonces** se sirve desde caché; una invalidación por evento de catálogo refresca el resultado.

### Story 3.3: Listado de reservas del agente con detalle

> **Trazabilidad:** HU1-5 → **FR-13** → `AC-E3.3.x` · **Obligatorio**
> **Porqué:** el agente concilia comisiones; debe ver el detalle completo de las reservas de **sus** hoteles y solo de ellos.

Como **agente**,
quiero **listar las reservas de mis hoteles y ver su detalle**,
para **conciliar comisiones**.

**Acceptance Criteria:**

**AC-E3.3.1 — Contenido del listado y detalle**
**Dado** reservas en los hoteles del agente
**Cuando** consulto el listado
**Entonces** cada ítem muestra hotel, habitación, estancia, estado y precio; el detalle añade huéspedes y contacto de emergencia.

**AC-E3.3.2 — Aislamiento (AC negativo)**
**Dado** reservas de hoteles de **otro** agente
**Cuando** consulto mi listado
**Entonces** esas reservas **no** aparecen (aislamiento resuelto server-side).

---

## Epic 4: Ciclo de vida de la reserva — cancelación con discreción

*Diferenciador (profundo) · valor más allá del enunciado · Fase 1 (núcleo de dominio).*

### Story 4.1: Solicitar cancelación con política sugerida

> **Trazabilidad:** — (no exigido por el enunciado) → **FR-14, FR-15** → `AC-E4.1.x` · **Diferenciador**
> **Porqué:** refleja la operación real de una agencia (inventario perecedero); la penalidad es sugerencia congelada, no imposición, para no perjudicar al viajero por la demora administrativa.

Como **viajero (o agente en su nombre)**,
quiero **solicitar la cancelación de una reserva confirmada indicando el motivo**,
para **iniciar el proceso y conocer la penalidad estimada**.

**Acceptance Criteria:**

**AC-E4.1.1 — Solicitud válida y penalidad sugerida congelada**
**Dado** una reserva `Confirmada` con estancia no iniciada
**Cuando** se solicita la cancelación con motivo (categoría + texto libre) e `Iniciador`
**Entonces** la reserva pasa a `CancelacionSolicitada`, se **congela** la `PenalidadSugerida` (ref = fecha de solicitud: `>=30` días → `0%`; `<30` días → `100%`) y se escribe `SolicitudCancelacionRegistrada` en el outbox.
**Y** la respuesta **incluye** la penalidad como valor informativo (no se cobra).

**AC-E4.1.2 — Solicitud duplicada (AC negativo)**
**Dado** una reserva con una solicitud de cancelación en curso
**Cuando** se solicita otra
**Entonces** responde `409`.

**AC-E4.1.3 — Estado no elegible (AC negativo)**
**Dado** una reserva que no está `Confirmada` (o con estancia ya iniciada)
**Cuando** se solicita la cancelación
**Entonces** responde `409`/`422` según el guard, sin cambiar de estado.

### Story 4.2: Resolver cancelación (aprobar / condonar / rechazar) con auditoría

> **Trazabilidad:** — → **FR-16** → `AC-E4.2.x` · **Diferenciador**
> **Porqué:** el agente decide con criterio (determinismo vs juicio); la liberación de inventario al aprobar es el efecto de negocio que casi nadie prueba.

Como **agente del hotel**,
quiero **resolver una solicitud de cancelación con discreción**,
para **aplicar la penalidad, condonarla o rechazar, liberando inventario cuando corresponde**.

**Acceptance Criteria:**

**AC-E4.2.1 — Aprobar libera el slot (round-trip — el assert que importa)**
**Dado** una reserva `CancelacionSolicitada` que ocupa el único slot de `[D]`
**Cuando** el agente aprueba (aplicando o condonando)
**Entonces** la reserva pasa a `Cancelada`, se borran las `NochesHabitacion` de la estancia, `count(slots disponibles en [D]) == 1`
**Y** una **nueva** `CrearReserva` sobre `[D]` ahora responde `201`
**Y** se registra la `PenalidadDecidida` (flag default/override + quién decidió) y se escribe `ReservaCancelada` en el outbox.

**AC-E4.2.2 — Rechazar no toca slots**
**Dado** una reserva `CancelacionSolicitada`
**Cuando** el agente rechaza con motivo
**Entonces** la reserva vuelve a `Confirmada`, no se libera ningún slot y se escribe `SolicitudCancelacionRechazada` en el outbox.

**AC-E4.2.3 — Doble resolución / doble liberación (AC negativo)**
**Dado** una reserva ya resuelta
**Cuando** llega una segunda resolución concurrente (mismo `rowVersion`)
**Entonces** responde `409` (guard de estado + `rowVersion`); `count(slots)` **no** sube a `2` (guard contra doble liberación).

**AC-E4.2.4 — Agente ajeno (AC negativo)**
**Dado** un agente que no es dueño del hotel de la reserva
**Cuando** intenta resolver
**Entonces** responde `403`.

### Story 4.3: Atajo de un paso, ciclo de vida y visibilidad

> **Trazabilidad:** — → **FR-17** → `AC-E4.3.x` · **Diferenciador**
> **Porqué:** el agente que atiende por teléfono resuelve en una operación; la auditoría no debe quedar con decisiones huérfanas.

Como **agente**,
quiero **solicitar y resolver una cancelación en una sola operación y ver la antigüedad de las pendientes**,
para **atender al viajero por teléfono sin perder trazabilidad**.

**Acceptance Criteria:**

**AC-E4.3.1 — Atajo auditado**
**Dado** una reserva `Confirmada`
**Cuando** el agente ejecuta el atajo (solicitar + resolver)
**Entonces** se registran **ambos** eventos (solicitud y resolución) para auditoría.

**AC-E4.3.2 — Guards del ciclo de vida**
**Dado** cualquier transición
**Cuando** se intenta salir del ciclo `Confirmada → CancelacionSolicitada → {Cancelada | Confirmada}`
**Entonces** una transición no permitida se rechaza por guard.

**AC-E4.3.3 — Antigüedad visible**
**Dado** solicitudes pendientes
**Cuando** se listan
**Entonces** cada una expone sus "días en espera"; no hay expiración automática.

---

## Epic 5: Notificaciones por correo sin pérdida ni duplicado

*Diferenciador (profundo) · contiene HU2-5 OBLIGATORIO · Fase 2 · no recortable.* Aquí se salda la deuda `[DEUDA-VERIF:E5]` de E1.

### Story 5.1: Notificar la confirmación de reserva (idempotente)

> **Trazabilidad:** HU2-5 → **FR-19** · **[SALDA: AC-E1.6.3 (E5)]** → `AC-E5.1.x` · **Obligatorio**
> **Porqué:** HU2-5 es criterio del enunciado; el efecto exactamente-una-vez (dedupe del consumidor) es lo que E1 dejó como deuda. Es el cierre end-to-end que Mary marcó en riesgo de partirse.

Como **huésped y agente**,
quiero **recibir un correo al confirmarse la reserva, exactamente una vez**,
para **tener constancia inmediata sin spam de duplicados**.

**Acceptance Criteria:**

**AC-E5.1.1 — Entrega end-to-end (outcome obligatorio)**
**Dado** una reserva confirmada (evento `ReservaConfirmada` en el outbox de E1)
**Cuando** el relay publica y `Notificaciones.Worker` consume
**Entonces** se envía un correo al huésped y otro al agente (SMTP).

**AC-E5.1.2 — Idempotencia del consumidor (salda la deuda de E1)**
**Dado** el mismo evento entregado N veces (`deliveries >= 1`, at-least-once)
**Cuando** el worker lo procesa deduplicando por `(MessageId, version)` en Redis (SETNX + TTL)
**Entonces** se envía `exactamente 1` correo por destinatario (`efecto == 1`).

**AC-E5.1.3 — Sin pérdida tras caída del broker (G3)**
**Dado** el broker caído durante una ráfaga
**Cuando** se recupera
**Entonces** el `100%` de los eventos pendientes se entrega; `0` correos perdidos.

### Story 5.2: Notificar la solicitud de cancelación

> **Trazabilidad:** — → **FR-20** → `AC-E5.2.x` · **Diferenciador**
> **Porqué:** el viajero necesita un acuse con la estimación, marcada como tal para no confundirla con el cobro final.

Como **viajero y agente**,
quiero **ser avisado cuando se solicita una cancelación**,
para **conocer la penalidad estimada y (el agente) saber que hay algo por resolver**.

**Acceptance Criteria:**

**AC-E5.2.1 — Acuse con estimación**
**Dado** una `SolicitudCancelacionRegistrada`
**Cuando** el worker la consume
**Entonces** el viajero recibe un acuse que **incluye** la penalidad estimada, etiquetada explícitamente como estimación (no cobro final); el agente recibe aviso de "por resolver".

### Story 5.3: Notificar la resolución de la cancelación

> **Trazabilidad:** — → **FR-21** → `AC-E5.3.x` · **Diferenciador**
> **Porqué:** el desenlace (aplicar/condonar/rechazar) debe comunicarse sin ambigüedad, incluida la nota del agente si difiere de la estimación.

Como **viajero**,
quiero **recibir el desenlace de mi solicitud de cancelación**,
para **saber la penalidad final, si fue condonada, o que mi reserva sigue en pie**.

**Acceptance Criteria:**

**AC-E5.3.1 — Aprobación / condonación**
**Dado** una `ReservaCancelada`
**Cuando** el worker la consume
**Entonces** el viajero recibe la penalidad **final** (con nota del agente si difiere de la sugerida) o el aviso de condonación.

**AC-E5.3.2 — Rechazo (mensaje inequívoco)**
**Dado** una `SolicitudCancelacionRechazada`
**Cuando** el worker la consume
**Entonces** el viajero recibe un correo indicando que la reserva **sigue** `Confirmada` y el motivo del rechazo.

---

## Epic 6: Seguridad y acceso

*Núcleo (acceso) + diferenciador (OWASP) · Fase 1→2.*

### Story 6.1: Autenticación JWT/OIDC

> **Trazabilidad:** — → **FR-22** → `AC-E6.1.x` · **Obligatorio**
> **Porqué:** el enunciado exige JWT/OAuth2; sin token válido no se opera.

Como **operador del sistema**,
quiero **que toda operación exija un token válido**,
para **impedir el acceso no autenticado**.

**Acceptance Criteria:**

**AC-E6.1.1 — Sin token (AC negativo)**
**Dado** una petición sin token o con token inválido/expirado
**Cuando** llega al Gateway
**Entonces** responde `401` (issuer/audience/expiración verificados).

### Story 6.2: Autorización por rol (RBAC)

> **Trazabilidad:** — → **FR-23** → `AC-E6.2.x` · **Obligatorio**
> **Porqué:** roles `Agente`/`Viajero` separan capacidades; se resuelve server-side.

Como **operador del sistema**,
quiero **autorizar por rol en cada endpoint**,
para **que cada actor solo haga lo suyo**.

**Acceptance Criteria:**

**AC-E6.2.1 — Rol sin permiso (AC negativo)**
**Dado** un usuario autenticado con rol sin permiso para la operación
**Cuando** la invoca
**Entonces** responde `403` (policies .NET server-side).

### Story 6.3: Aislamiento entre agentes

> **Trazabilidad:** — → **FR-24** → `AC-E6.3.x` · **Obligatorio**
> **Porqué:** un agente no puede leer ni modificar recursos de otro; es control de acceso de datos, no solo de rol.

Como **agente**,
quiero **que mis hoteles y reservas sean invisibles e inmutables para otros agentes**,
para **proteger mi operación**.

**Acceptance Criteria:**

**AC-E6.3.1 — Lectura ajena (AC negativo)**
**Dado** un recurso (hotel/reserva) de otro agente
**Cuando** intento leerlo
**Entonces** responde `403`/`404` (sin filtrar existencia).

**AC-E6.3.2 — Escritura ajena (AC negativo)**
**Dado** un recurso de otro agente
**Cuando** intento modificarlo
**Entonces** responde `403`; el recurso no cambia.

### Story 6.4: Endurecimiento OWASP (8 prácticas)

> **Trazabilidad:** NFR-4 · G6 → `AC-E6.4.x` · **Diferenciador · Fase 2**
> **Porqué:** la vacante exige OWASP; se implementan y documentan 8 prácticas mapeadas al Top 10 (2021), con cero secretos en el repo.

Como **responsable de seguridad**,
quiero **8 prácticas mapeadas a OWASP y sin secretos en el repositorio**,
para **demostrar seguridad proporcional y verificable**.

**Acceptance Criteria:**

**AC-E6.4.1 — Prácticas implementadas**
**Dado** el sistema desplegado
**Cuando** se audita
**Entonces** están activas y documentadas: rate limiting, validación/anti-inyección (FluentValidation + EF parametrizado), manejo de secretos (Dapr Secrets/Key Vault), HTTPS/HSTS + CORS allowlist, logging de eventos de seguridad sin PII, protección de PII.

**AC-E6.4.2 — Cero secretos en el repo (CI)**
**Dado** un push
**Cuando** corre gitleaks/SAST en CI
**Entonces** `0` hallazgos de secretos y `0` hallazgos críticos.

---

## Epic 7: Observabilidad de extremo a extremo

*Diferenciador · recortable · Fase 2.*

### Story 7.1: Traza distribuida propagada extremo a extremo

> **Trazabilidad:** — → **FR-25** → `AC-E7.1.x` · **Diferenciador**
> **Porqué:** ante un fallo hay que ver el span exacto; el `trace-id` técnico (W3C) debe atravesar el sidecar Dapr, distinto del id de correlación de negocio.

Como **operador**,
quiero **seguir una petición por todos los saltos con un trace-id**,
para **localizar el span exacto donde algo falla**.

**Acceptance Criteria:**

**AC-E7.1.1 — Propagación completa**
**Dado** una petición que entra por el Gateway
**Cuando** recorre `Gateway → servicio → sidecar Dapr → Worker`
**Entonces** el mismo `traceparent` (W3C) aparece en todos los spans, visible en el dashboard de Aspire.

**AC-E7.1.2 — Span de fallo visible**
**Dado** un fallo en un servicio
**Cuando** abro la traza
**Entonces** el waterfall marca el servicio/operación exacto con su excepción.

### Story 7.2: Métricas p95/p99 por endpoint

> **Trazabilidad:** — → **FR-26** → `AC-E7.2.x` · **Diferenciador**
> **Porqué:** detectar degradación (especialmente de la búsqueda bajo carga de escritura, G7) exige percentiles, no promedios.

Como **operador**,
quiero **métricas de duración p95/p99 por endpoint**,
para **detectar degradación de latencia**.

**Acceptance Criteria:**

**AC-E7.2.1 — Histograma de duración**
**Dado** tráfico sobre los endpoints
**Cuando** consulto las métricas
**Entonces** hay histograma de duración por endpoint con p95/p99 disponibles.

---

## Epic 8: Nube por IaC (con compuerta)

*Recortable — primero en recortarse. No bloquea ningún criterio obligatorio.*

### Story 8.1: Aprovisionar Azure por Terraform

> **Trazabilidad:** — → **NFR-6** → `AC-E8.1.x` · **Recortable · Fase 3**
> **Porqué:** demuestra cloud-native e IaC; el despliegue es exclusivamente por Terraform (ADR-008), sin click-ops.

Como **responsable de despliegue**,
quiero **aprovisionar la infraestructura de Azure con Terraform**,
para **desplegar de forma reproducible y sin provisión manual**.

**Acceptance Criteria:**

**AC-E8.1.1 — IaC ejecutable**
**Dado** el módulo Terraform
**Cuando** ejecuto `terraform plan`/`apply` (o `plan` en CI)
**Entonces** describe ACA + Azure SQL + Cache for Redis + Service Bus + Key Vault + App Insights, sin credenciales en el código.

**AC-E8.1.2 — Cloud-agnostic por Dapr (AC negativo de acoplamiento)**
**Dado** el cambio de broker local→nube
**Cuando** se despliega
**Entonces** solo cambia el component YAML de Dapr; `0` cambios de código de aplicación.
