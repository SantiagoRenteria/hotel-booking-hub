---
stepsCompleted: [1, 2, 3, 4, 5, 6, 7, 8]
lastStep: 8
status: 'complete'
completedAt: '2026-07-08'
inputDocuments:
  - docs/planning-artifacts/prds/prd-hotel-booking-hub-2026-07-08/prd.md
  - docs/specs/spec-hotel-booking-hub/SPEC.md
  - docs/specs/spec-hotel-booking-hub/glossary.md
  - docs/specs/spec-hotel-booking-hub/architecture-diagrams.md
  - docs/specs/spec-hotel-booking-hub/concurrency-and-messaging.md
  - docs/specs/spec-hotel-booking-hub/stack-and-conventions.md
  - docs/specs/spec-hotel-booking-hub/security-and-quality.md
  - docs/specs/spec-hotel-booking-hub/patterns.md
  - docs/specs/spec-hotel-booking-hub/decisions-adr.md
  - docs/specs/spec-hotel-booking-hub/delivery-and-testing.md
  - docs/DOCUMENTO-BASE.md
workflowType: 'architecture'
project_name: 'hotel-booking-hub'
user_name: 'Santiago'
date: '2026-07-08'
---

# Architecture Decision Document

_This document builds collaboratively through step-by-step discovery. Sections are appended as we work through each architectural decision together._

## Resumen ejecutivo

`hotel-booking-hub` es el back end de reservas de una agencia (single-tenant). El problema difícil no es el CRUD: es **garantizar cero overbooking bajo concurrencia** sin sacrificar el rendimiento de lectura.

**Decisión estructural clave:** la reserva, sus **slots de inventario** (`NochesHabitacion`) y su **evento de outbox** se escriben en **una sola transacción local** en el BC de Reservas; el invariante lo garantiza el **motor de datos** (el índice `UNIQUE(HabitacionId, Noche)` arbitra el conflicto en el INSERT), nunca la lógica de aplicación.

**Trade-offs que más pesan** (cada uno con su ADR): índice único + READ COMMITTED en vez de `SERIALIZABLE` (ADR-016, protege G7); UUID v7 no-clustered + clustering key `Seq` secuencial (ADR-017, evita fragmentación); **outbox manual at-least-once** con dedupe idempotente en el consumidor (no exactly-once en el wire); 2 Bounded Contexts que solo hablan por eventos; mediator propio (ADR-005/018, sin dependencia comercial).

**Consistencia asimétrica a propósito:** fuerte-local para el invariante (una tx, un motor); eventual entre BCs para todo lo demás (proyecciones, notificaciones).

**Estado (dos ejes):** **Diseño completo** (16/16, confianza alta). **Ejecución no validada** — condicionada a 2 spikes de Sprint 0 (money test G1×G3 + wiring del mediator). *"Diseño completo" ≠ "diseño validado".*

> Las secciones 1-6 siguen el proceso de diseño paso a paso; los ADR están en `docs/adr/`; el contrato canónico en `docs/specs/`.

## Project Context Analysis

### Requirements Overview

**Functional Requirements (26 FR / 7 features):**
- F1 · Gestión de hoteles e inventario (FR-1…7) — CRUD + soft delete + habilitar/deshabilitar; CAP-1/2.
- F2 · Búsqueda y reserva (FR-8…12) — búsqueda por disponibilidad real + creación-confirmación atómica + precio; CAP-4/5.
- F3 · Reservas del agente y ciclo de vida/cancelación (FR-13…17) — listado del agente + solicitud/política/resolución con discreción auditada; CAP-3/10/11.
- F4 · Anti-overbooking (FR-18) — invariante en el motor de datos; CAP-6. **Núcleo del diseño.**
- F5 · Notificaciones (FR-19…21) — correo sin pérdida ni duplicado, reserva y cancelación; CAP-7.
- F6 · Seguridad y acceso (FR-22…24) — 401/403 + aislamiento entre agentes; CAP-8.
- F7 · Observabilidad (FR-25/26) — trazas distribuidas + p95/p99; CAP-9.

**Non-Functional Requirements (8 NFR):** NFR-1 rendimiento/escala (lectura vía proyección+Redis), NFR-2 concurrencia/consistencia, NFR-3 fiabilidad de mensajería, NFR-4 seguridad, NFR-5 observabilidad, NFR-6 portabilidad, NFR-7 mantenibilidad (Clean Arch+DDD+CQRS, mediator propio, TDD ≥80%), NFR-8 contratos (OpenAPI+Scalar).

**Scale & Complexity:**
- Primary domain: back end / API + sistema distribuido dirigido por eventos.
- Complexity level: media-alta — por naturaleza distribuida + invariante fuerte + eventos sin pérdida, no por volumen (~10k reservas/día, ~0,12 writes/s).
- La contención relevante NO es de throughput agregado sino un **hotspot de filas específicas**: muchas solicitudes sobre la misma habitación/fechas en temporada alta. Esto —no el promedio diario— dimensiona la estrategia de concurrencia y reintentos.
- Componentes desplegables: ~6 (Gateway YARP, Hoteles.Api, Reservas.Api, Notificaciones.Worker) + infra (SQL Server ×2, Redis, broker, sidecars Dapr).

### Modelo de consistencia (asimetría deliberada)

El sistema ofrece **dos garantías de distinta dureza, a propósito**:
- **Consistencia fuerte local** para el invariante anti-overbooking, resuelto dentro de una transacción de una sola base de datos (la del BC dueño de la disponibilidad).
- **Consistencia eventual entre Bounded Contexts** para todo lo demás (notificaciones, proyecciones de lectura, reflejo cruzado) vía outbox + eventos.

Corolario de diseño: la **cancelación vive del lado eventual** aunque la creación de la reserva sea fuerte-local; esta asimetría es correcta y debe justificarse explícitamente en cada ADR que la toque (evita que se exija erróneamente "cancelación instantánea y fuerte"). Además, el concern de consistencia se separa en dos perfiles con mitigaciones distintas:
- **Escritura:** evitar overbooking (invariante en el motor).
- **Lectura / staleness:** que la búsqueda no mienta durante la ventana de contención (la proyección/caché puede mostrar disponible lo ya tomado → 409 evitables). Condiciona si la proyección es síncrona, por eventos, o con SLA de staleness documentado.

### Riesgos de calidad en el radar (antes de diseñar)

- **Amplificación de reintentos bajo alta contención (SQL deadlock 1205):** N concurrentes (G1) no solo producen 409 de negocio; producen deadlocks con rollback+retry. Sin presupuesto de reintentos explícito, "G1 pasa" puede esconder agotamiento del pool y degradación del p95/p99 de búsqueda — justo lo que G7 promete estable. Riesgo alto a N≥50; contamina la lectura de G1 y G7 a la vez.
- **Staleness de lectura CQRS** (arriba): UX/negocio, no overbooking.
- **Versionado del esquema de eventos:** el outbox puede reproducir un evento con esquema viejo tras un deploy con esquema nuevo; un cambio no coordinado rompe consumidores en silencio (contra G3).

### Decisiones de diseño que este documento debe resolver

Alimentan los siguientes pasos del workflow y sus ADRs (no se cierran aquí):
1. **Frontera de propiedad de datos del invariante** — dónde vive `NochesHabitacion` y quién es dueño de la disponibilidad noche a noche (determina si el invariante es transacción local o cruza servicios).
2. **[Revisita ADR-003] SERIALIZABLE vs. índice único como árbitro** — el `UNIQUE(HabitacionId, Noche)` ya captura el conflicto en el INSERT; SERIALIZABLE solo se justifica ante un patrón check-then-act. Decidir el nivel de aislamiento + **política de retry 1205** (capa, intentos, backoff, interacción con outbox/idempotencia). *Toca un ADR ya cerrado → requiere visto bueno de Santiago.*
3. **Layout del UUID v7 en SQL Server** — clustering key secuencial vs. `uniqueidentifier` naive (fragmentación de páginas bajo inserción concurrente → impacta G7). Afecta PKs y FKs; decisión de la primera migración.
4. **Contrato y versionado del esquema de eventos** — el evento es la API real entre BCs; envelope, semver del tipo, compatibilidad hacia atrás, dueño del schema.
5. **Pipeline de behaviors del mediator propio** — registro de handlers (scan vs. explícito) y punto de entrada de validación/logging/transacción/outbox desde el día uno.
6. **Outbox manual vs. Dapr nativo** (`dapr/outbox`) — define si `Reservas.Infrastructure` tiene tabla+relay propios o delega a la sidecar (cambia el diseño de datos).
7. **Pureza del cálculo de precio** — domain service sin I/O (unit-testeable) vs. dependiente de estado persistido (condiciona el costo del TDD del flujo crítico).

### Matriz núcleo / diferenciador / recortable

Bisagra hacia el diseño: cada concern trazado al criterio de evaluación ("claridad del razonamiento", core impecable + pocos diferenciadores). Regla: *si se degrada, ¿quién lo siente primero?*

| Concern | Clasificación | Por qué |
|---|---|---|
| Anti-overbooking (slots + transacción) | **Núcleo intocable** | Si falla, el agente vende dos veces la misma habitación: rompe la promesa central. |
| Creación-confirmación atómica + precio | **Núcleo** | Es la operación de negocio y el flujo crítico TDD. |
| Ciclo de vida / cancelación con discreción | **Diferenciador (profundo)** | Auto-inventado; demuestra criterio de dominio. Se entrega completo. |
| Notificaciones sin pérdida/duplicado (outbox+idempotencia) | **Diferenciador (profundo)** | Nivel senior; Fase 2. |
| Seguridad (JWT/RBAC/OWASP) | **Núcleo (acceso) + diferenciador (OWASP)** | Acceso 401/403 y aislamiento son núcleo; el mapeo OWASP completo es profundidad de F2. |
| Observabilidad (OTel end-to-end) | **Diferenciador** | Aporta trazabilidad; no bloquea el core. Fase 2. |
| Nube (Terraform/ACA) | **Recortable (con compuerta)** | Primero en recortarse; queda como IaC documentada. |

### Supuestos no verificados (marcados antes de diseñar)

- **[VERIFICAR] Modo de evaluación:** se asume que el evaluador ejecuta vía `docker compose up` pero **no ejecuta el despliegue a Azure**; por eso recortar Terraform es decisión de alcance y no riesgo de entrega. Si llegara a desplegar, cambia.
- **[VERIFICAR] Origen del inventario:** se asume inventario **propio** de la agencia (una BD de hoteles), no integración con un channel manager externo. Si hubiera terceros, aparece un concern de resiliencia (retries/circuit breaker) hoy fuera de alcance.
- **[ASSUMPTION heredado del SPEC]** penalidad sugerida congelada en fecha de solicitud; sin cobro real (monto adeudado).

## Starter Template Evaluation

### Primary Technology Domain

API / back end distribuido en **C# · .NET 10 (LTS)** con orquestación local vía **.NET Aspire (Aspire 13)**. Preferencias técnicas ya fijadas y justificadas en el SPEC (`stack-and-conventions.md` + ADRs), no en un `project-context.md`; este paso las honra y ancla las versiones verificadas.

### Versiones verificadas (jul 2026)

- **.NET 10** — GA 11-nov-2025, **LTS** hasta nov-2028. Satisface el "`.NET 8 o superior`" del enunciado con la LTS vigente.
- **Aspire 13** (`Aspire.ProjectTemplates` 13.4.x) — requiere SDK .NET 10 para el AppHost en C#; producto desacoplado del versionado de .NET. **NuGet-only vía `Aspire.AppHost.Sdk`** — el modelo `dotnet workload install aspire` está deprecado y NO se usa.

### Starter Options Considered

- **`aspire-starter` (Starter App)** — descartado: scaffoldea una muestra Blazor Web + API + tests que no aplica a un back end puro con estructura DDD a medida. Decisión registrada en **ADR-015** ("por qué no `aspire-starter`") para que la ausencia del sample se lea como criterio, no como omisión.
- **`aspire-apphost` + `aspire-servicedefaults` (elegido)** — aporta solo lo transversal: AppHost (topología, wiring, dashboard) y ServiceDefaults (OpenTelemetry, health checks, service discovery, resiliencia por defecto). Los servicios de dominio se añaden a mano siguiendo la estructura del repo.
- **Sin Aspire (solo docker-compose)** — descartado: se pierde el dev-loop y el dashboard OTel; contradice ADR-007.
- **Colapsar a un solo servicio (walking skeleton mínimo)** — descartado: el SPEC fija ≥2 Bounded Contexts como microservicios independientes (ADR-001; el enunciado premia separar ≥2 dominios). Se conserva la estructura completa; lo que se prioriza es el *esfuerzo*, no se recorta la estructura (ver "Secuencia F0").

### Selected Starter: Aspire AppHost + ServiceDefaults (bespoke solution)

**Rationale:** máxima señal de criterio para una prueba evaluada por claridad — se adopta lo transversal (orquestación + telemetría reproducibles) y se rechaza el scaffold de muestra que ensuciaría el repo. La estructura de solución la manda el contrato, no la plantilla.

**Initialization Command (verificar versiones vigentes al ejecutar):**

```bash
# 0) Base de la solución + gobernanza de versiones (desde el PRIMER commit)
dotnet new sln --name HotelBookingHub
dotnet new install Aspire.ProjectTemplates      # v13.x (NuGet-only; sin workload)
#   + raíz: Directory.Packages.props  (Central Package Management: una versión por paquete)
#   + raíz: Directory.Build.props     (<TargetFramework>net10.0</TargetFramework>, Nullable=enable, ImplicitUsings=enable)

# 1) Piezas transversales de Aspire (NO el starter con sample)
dotnet new aspire-servicedefaults --name ServiceDefaults --output src/AppHost/ServiceDefaults
dotnet new aspire-apphost        --name AppHost         --output src/AppHost/AppHost
dotnet sln add src/AppHost/ServiceDefaults src/AppHost/AppHost

# 2) Servicios de dominio (Minimal API) + worker + gateway
dotnet new webapi --use-minimal-apis --name Hoteles.Api  --output src/Servicios/Hoteles/Hoteles.Api
dotnet new webapi --use-minimal-apis --name Reservas.Api --output src/Servicios/Reservas/Reservas.Api
dotnet new worker --name Notificaciones.Worker           --output src/Servicios/Notificaciones/Notificaciones.Worker
dotnet new web    --name ApiGateway                      --output src/ApiGateway   # 'web' vacío (NO webapi) + Yarp.ReverseProxy
#   + bibliotecas de capa por servicio (.Application/.Domain/.Infrastructure) y src/Comun/HotelBookingHub.Comun

# 3) Por CADA ejecutable, referencia explícita desde el AppHost (habilita Projects.* del source-generator)
dotnet add src/AppHost/AppHost/AppHost.csproj reference \
  src/Servicios/Hoteles/Hoteles.Api src/Servicios/Reservas/Reservas.Api \
  src/Servicios/Notificaciones/Notificaciones.Worker src/ApiGateway

# 4) Limpieza del scaffold: borrar WeatherForecast.cs y el endpoint /weatherforecast de cada Program.cs;
#    revisar Properties/launchSettings.json (Aspire orquesta los puertos vía AppHost).
```

**Architectural Decisions Provided by Starter:**

- **Language & Runtime:** C# / .NET 10 LTS; `Nullable`+`ImplicitUsings` centralizados en `Directory.Build.props`.
- **Gobernanza de dependencias:** **Central Package Management** (`Directory.Packages.props`) desde el primer commit — evita drift de versiones entre servicios (EF Core, StackExchange.Redis, Dapr.Client).
- **Observabilidad (ServiceDefaults):** OpenTelemetry (trazas + métricas + logs) preconfigurado → base de CAP-9 / FR-25/26 / NFR-5.
- **Salud y resiliencia:** health checks (`/health`, `/alive`) y `Microsoft.Extensions.Http.Resilience` por defecto → alinea ADR-010 y el smoke test de `docker-compose` (G2).
- **Service discovery + wiring:** el AppHost declara recursos (SQL Server, Redis, broker, sidecars Dapr) y los inyecta por configuración; `ProjectReference` por servicio es requisito para el source-generator `Projects.*`.
- **OpenAPI nativo:** .NET 10 usa `Microsoft.AspNetCore.OpenApi` (sin Swashbuckle); UI vía Scalar (ADR-011).
- **Code organization:** estructura impuesta por `stack-and-conventions.md`, no por la plantilla.

### Secuencia F0 (estructura completa, esfuerzo primero al core)

La estructura nace **completa** (honra la constraint de ≥2 BC y hace que el C4 sea real), pero el orden de trabajo prioriza valor: el **primer story con TDD ataca el core** (cálculo de precio + creación de reserva + anti-overbooking en `Reservas` sobre SQL Server real con Testcontainers). El cableado de Gateway (YARP) y Notificaciones.Worker se completa después dentro de F0/F1, sin bloquear el core. Métrica del arranque: minimizar el tiempo hasta el **primer test rojo de dominio**.

### Acciones de documentación derivadas (feeding README/ADRs)

- **ADR-015** — "por qué no `aspire-starter`" (contexto/decisión/consecuencias).
- **README raíz** — abre con el **C4 de Contenedores** (imagen que carga sin clonar) + **árbol de carpetas comentado** (≤20 líneas, nombres de carpeta = nombres del diagrama = conceptos de negocio) + enlace gancho al ADR-015.

**Nota:** la inicialización con estos comandos (versiones re-verificadas al ejecutar) es la **primera historia de implementación** (Fase 0 → Fase 1).

## Core Architectural Decisions

### Decision Priority Analysis

**Critical (bloquean implementación):** frontera de datos del invariante · arbitraje de concurrencia · estrategia de claves · outbox · pipeline del mediator.
**Important (moldean la arquitectura):** contrato/versionado de eventos · pureza del cálculo de precio · política de retry · liberación de slots · idempotencia/orden de proyección.
**Deferred (con racional):** compensación por deshabilitación con reservas activas (gancho diseñado, no implementado) · chaos test del outbox (F2) · read model MongoDB (ADR-013) · nube Azure/Terraform (F3) · waitlist (futuro).

### Data Architecture

- **Propiedad de datos (frontera BC):** **Reservas** es dueño de `NochesHabitacion` (slots) y de la **`ProyeccionHabitacion`** (read model por eventos de Hoteles); **Hoteles** es dueño del catálogo `Habitacion`/`Hotel`. El invariante anti-overbooking es **transacción local** en la BD de Reservas; sin acoplamiento síncrono.
- **Arbitraje de concurrencia (reescribe ADR-003):** INSERT de los N slots bajo **READ COMMITTED**; la violación de **`UNIQUE(HabitacionId, Noche)`** es el árbitro. Clasificación por **`SqlException.Number`** (pattern matching, nunca parsear mensaje):
  - **`2627`/`2601`** (violación de único) → **409 inmediato, cero retry** (determinístico: otro ganó).
  - **`1205`** (deadlock victim) → **retry acotado** (3 intentos, backoff+jitter); re-ejecuta el handler completo (idempotente). Sin SERIALIZABLE se esperan *más* 1205, no menos — es el comportamiento nominal.
  - Los slots del batch se insertan en **orden determinístico** (`ORDER BY HabitacionId, Noche`) para minimizar deadlocks. La reserva multi-slot es **todo-o-nada** (atomicidad por transacción).
  - *Supuesto:* `Habitacion` es unidad física individual (no categoría con cupo N); si evolucionara a inventario por tipo, el árbitro cambia (contador + concurrencia optimista).
- **Liberación de slots:** al **aprobar** una cancelación, las filas de `NochesHabitacion` se **borran físicamente** (DELETE) → el `UNIQUE` se mantiene simple y la noche vuelve a ser reservable limpiamente. El estado del ciclo de vida vive en el aggregate `Reserva`, no en los slots.
- **Estrategia de claves (ajusta constraint UUID v7):** identidad de dominio = **UUID v7** (`Guid.CreateVersion7()`), PK **NO-clustered**, expuesta en API y eventos. Clustering key = columna secuencial interna **`Seq bigint IDENTITY`** (evita la fragmentación del v7 en el orden de `uniqueidentifier`). `Seq` es **shadow property**: nunca cruza la frontera del BC ni aparece en DTOs/eventos/logs; las **FK apuntan al Guid**. `NochesHabitacion` no usa surrogate — su clave clustered natural es el compuesto `(HabitacionId, Noche)`. *Matiz:* la anti-fragmentación aplica a entidades con surrogate secuencial; `NochesHabitacion` se fragmenta por actividad de negocio y se gestiona con **fill factor** + mantenimiento de índice.
- **Config EF Core (SQL Server):** `HasKey(x=>x.Id).IsClustered(false)` + `HasIndex(x=>x.Seq).IsUnique().IsClustered()` + `Property(x=>x.Seq).UseIdentityColumn()`; `NochesHabitacion`: `HasKey(x=>new{ x.HabitacionId, x.Noche })`.
- **Migraciones:** EF Core 10 code-first; versión del provider fijada por CPM.
- **Caché:** Redis (ADR-012) — disponibilidad + inbox de idempotencia (message-id/TTL) + Dapr state store.

### Authentication & Security

- **JWT/OIDC propio** + **RBAC server-side** (Agente/Viajero), también en nube (ADR-006). 401 sin token, 403 sin permiso, aislamiento agente↔agente en autorización.
- **Cero secretos en repo:** Dapr Secrets (local) / Key Vault (nube). SAST + gitleaks en CI. 8 prácticas OWASP (F2).

### API & Communication Patterns

- **REST + OpenAPI nativo** + **Scalar** (ADR-011); `/api/v1/`; **Problem Details RFC 7807**; `Result<T>` en flujos esperados.
- **Comunicación entre BCs = solo eventos** vía Dapr pub/sub (ADR-002). **Contrato de eventos:** envelope `{ id, type, version, occurredAt, traceId, data }`; **semver en `type`**; compatibilidad hacia atrás; Reservas dueño del schema que consume Notificaciones.
- **Outbox manual (at-least-once por diseño):** tabla `OutboxMessages` en la **misma transacción** EF Core que el cambio de dominio; el **`MessageId` se genera una sola vez antes del `TransactionBehavior`**; `UNIQUE(OutboxMessages.MessageId)`. **Relay `BackgroundService`** con polling y **lease-expiry/re-claim por antigüedad** (no solo por estado → sin mensajes huérfanos); publica a Dapr pub/sub. La **no-duplicación se garantiza en el efecto** (dedupe del consumidor por message-id en Redis, SETNX), no en el wire. (Dapr outbox nativo descartado: acoplaría persistencia al state store de Dapr.)
- **Proyección `ProyeccionHabitacion`:** handler **idempotente y ordenado** — descarta eventos viejos por versión/secuencia (evita "wrong forever" por reordenamiento de Dapr). **Job de reconciliación/rebuild** desde el event-log como mitigante de corrupción. La disponibilidad de búsqueda se filtra best-effort por esta proyección (consistencia eventual; el invariante duro sigue en el motor).
- **Gateway:** YARP (`dotnet new web` + `Yarp.ReverseProxy`) — enruta/agrega, sin lógica de negocio.

### Frontend Architecture

No aplica — la entrega es exclusivamente back end.

### Infrastructure & Deployment

- **Dev:** .NET Aspire (AppHost declara SQL Server ×2, Redis, broker, sidecars Dapr) + dashboard OTel.
- **Reproducibilidad:** `docker-compose` a mano + smoke test de `/health` en CI (ADR-007).
- **Nube (F3, con compuerta):** ACA + Azure SQL + Cache for Redis + Service Bus + Key Vault + App Insights, **solo por Terraform** (ADR-008).
- **CI/CD:** GitHub Actions — build + test (unit + Testcontainers.MsSql) + gitleaks/SAST + Newman + smoke test de compose.

### Patrón del mediator (propio)

Registro de handlers por **scan de assembly**; pipeline **`Validation → Logging/Tracing → Transaction → Outbox`** desde el día uno. El `TransactionBehavior` abre la transacción y aplica el retry 1205; solo comandos (las queries no).

### Cálculo de precio

**Domain service puro** `CalculadorPrecio` (sin I/O): `(costoBase + impuesto) × noches`. La penalidad de cancelación (0%/100% por antelación) es igualmente función pura. 100% unit-testeable → sostiene el TDD del flujo crítico.

### Decision Impact Analysis

**Secuencia (F0→F1):** (1) esqueleto + CPM/Directory.Build.props; (2) dominio de Reservas + `CalculadorPrecio` (TDD); (3) `NochesHabitacion` + índice único + INSERT-arbitraje + clasificación 2627/1205 + 409 (Testcontainers.MsSql); (4) `CrearReservaCommand` + outbox en la misma tx; (5) proyección Hoteles→Reservas por eventos (idempotente/ordenada); (6) cancelación (CAP-10/11, DELETE de slots al aprobar); (7) Gateway + Worker; (8) seguridad + observabilidad (F2).

**Dependencias cruzadas:** el arbitraje por índice único hace marginal el retry 1205 y protege el p95/p99 de G7 · la clustering key secuencial protege la inserción bajo el hotspot de G1 · outbox+idempotencia sostienen G3 · el contrato versionado + handler ordenado protegen a Notificaciones y la proyección.

**Gates de calidad derivados (grupo B):** unit de clasificación `2627/2601` vs `1205`; fault-injection compuesto del outbox (pérdida + duplicado-con-efecto + broker caído); orden/idempotencia del handler de proyección; lag p99 de proyección con evento canario; el test de concurrencia G1 (N=100) sobre SQL real.

**Supuestos de negocio marcados:** deshabilitar una habitación **retira la oferta futura pero NO cancela reservas confirmadas existentes** (se honran hasta su fin); el gancho de compensación queda diseñado, no implementado (revisable).

> **Sincronización pendiente del contrato:** estas decisiones **reescriben ADR-003** y **ajustan la constraint de UUID v7**. Se sincronizarán en `decisions-adr.md` y `stack-and-conventions.md` (+ **ADR-016** "arbitraje por índice único vs SERIALIZABLE" y **ADR-017** "claves: UUID v7 + clustering secuencial") tras cerrar este paso. _(Sincronizado en el commit `813a099`.)_

## Implementation Patterns & Consistency Rules

**Propósito (encuadre honesto):** estas reglas existen para que el código **no delate que se escribió a lo largo de muchas sesiones** (asistidas por IA) — coherencia de un solo autor, no orquestación de un enjambre de agentes. La mayoría ya es contrato en `stack-and-conventions.md`; aquí se consolidan como patrones, se añaden las derivadas de las decisiones de arquitectura, y se materializan en un **`AGENTS.md`** canónico consumible por la herramienta de IA.

### Naming Patterns

**Regla mnemónica tri-idioma** (clasificatoria, no de estilo):
> **¿Qué es? → español sin tilde. ¿Cómo se implementa? → inglés. ¿Qué le digo al humano? → español con tilde.**

Sufijos de patrón permitidos (**lista cerrada**, verificable en CI): `Command, Query, Handler, Repository, Service, Validator, Behavior, Dto, Request, Response, Factory, Exception, Api, Worker, Middleware`.

| Elemento | Correcto | Incorrecto | Por qué falla |
|---|---|---|---|
| Comando | `CrearReservaCommand` | `CreateReservaCommand` | "Crear" es dominio → español sin tilde |
| Excepción | `HabitacionNoDisponibleException` | `...Excepcion` | "Exception" es sufijo de patrón (.NET), no se traduce |
| Mensaje al usuario | `"La habitación no está disponible"` | `"La habitacion no esta disponible"` | Los mensajes SÍ llevan tildes — es prosa, no identificador |

> **Trampa nº 1 (90% de los errores previsibles):** el mismo sustantivo cambia de forma según dónde viva — `habitacion` en un identificador (sin tilde, restricción de código), `habitación` en un string de negocio (con tilde, es prosa).

**Base de datos:** tablas PascalCase español plural (`Reservas`, `NochesHabitacion`, `OutboxMessages`); columnas PascalCase; PK lógica `Id` (UUID v7) + clustering key `Seq` (`bigint IDENTITY`, **shadow property**); FK `{Entidad}Id`→Guid; único árbitro `UX_NochesHabitacion_HabitacionId_Noche`.
**API:** recursos plural `/api/v1/hoteles`, param `{id}`, query camelCase, versionado por URL.

### Structure Patterns — *ya fijado*

Folder-per-bounded-context; `Comun` solo contratos (Result, mediator, behaviors, envelope, Problem Details); tests en `tests/` separando **UnitTests** (xUnit + InMemory/puro) e **IntegrationTests** (Testcontainers.MsSql — concurrencia y outbox viven aquí, nunca InMemory).

### Format Patterns

- **Sin wrapper:** recurso directo en éxito; **Problem Details RFC 7807** (`application/problem+json`) en error. Nada de `{data, error}` ni 200-con-error-en-body.
- **JSON `camelCase`** (System.Text.Json); enums como **string**; `DateTimeOffset` ISO 8601; `DateOnly` (`yyyy-MM-dd`) para estancia; `rowVersion` base64 en DTOs; **`decimal`** para dinero.

### Communication Patterns

- **Envelope** `{ id, type, version, occurredAt, traceId, data }`; `type` PascalCase español + **semver** (`ReservaConfirmada.v1`); compatibilidad hacia atrás. *(Consumidor real: `Notificaciones.Worker` — no es contrato teórico.)*
- **`MessageId` = `id` del envelope, asignado en el `TransactionBehavior` una sola vez al entrar, ANTES del loop de retry** (si se regenera en el retry de 1205, el `UNIQUE(OutboxMessages.MessageId)` no dedupea). Relay *at-least-once*; **dedupe en el consumidor** por `MessageId` en Redis (SETNX + TTL).
- **Handlers de proyección idempotentes y ordenados:** descartan eventos con `version`/secuencia anterior.
- Sin llamadas síncronas entre BCs.

### Process Patterns

- **Errores:** `Result<T>` en flujos esperados; excepciones solo para lo inesperado; middleware global → RFC 7807.
- **Mapeo excepción→HTTP:** `SqlException.Number` **2627/2601** → **409 sin retry**; **1205** → **retry** (3×, backoff+jitter) en `TransactionBehavior`; FluentValidation (`ValidationBehavior`) → **400**; sin token → **401**; sin permiso/agente ajeno → **403**; no encontrado → **404**. Clasificación por `Number`, **nunca** por mensaje.
- **`Result<T>` → HTTP centralizado:** una única extensión `Result<T>.ToHttpResult()`; endpoints con **`TypedResults` + union type explícito** (`Results<Ok<HotelDto>, NotFound, ValidationProblem>`), no `IResult` desnudo (mantiene el OpenAPI uniforme). `Result.Invalid` → `TypedResults.ValidationProblem` (`{errors:{campo:[msgs]}}`); excepción de negocio → `Problem` plano.
- **Retry selectivo** (ADR-010): solo deadlock 1205, SMTP (correo — dependencia externa real) y HTTP entre servicios (Polly/Http.Resilience). No en todos los métodos.
- **`CancellationToken`** obligatorio como último parámetro en toda firma que cruce I/O (handler, repository, endpoint), propagado siempre; analyzer **`CA2016`** en `TreatWarningsAsErrors`. Sufijo **`Async`** en métodos `async` de infraestructura/aplicación.

### Pipeline de behaviors del mediator (orden canónico)

```
1. LoggingBehavior      — scope de log/correlación, sin side effects
2. ValidationBehavior   — FluentValidation; corta con Result.Invalid antes de tocar la BD (no accede a DbContext)
3. TransactionBehavior  — abre la transacción, asigna MessageId (una vez), aplica retry 1205; SOLO comandos
4. Handler
```
Registro en **un único `AddMediatorPipeline(this IServiceCollection)`** en ese orden literal; las queries no pasan por `TransactionBehavior`.

### Observabilidad y logging

- **`traceId` del envelope = `Activity.Current.TraceId` (W3C Trace Context)**, generado por el middleware del Gateway y propagado por `Activity` ambient — **no** un `Guid` propio. Es distinto del `traceparent` que Dapr inyecta en el CloudEvent (correlación de negocio vs tracing técnico): se documenta la relación, no se confunden.
- **Logging estructurado** (Serilog + OTel sink) con enricher que vuelca `TraceId`/`SpanId` de `Activity.Current` automáticamente; `MessageId` y `AggregateId` como scope explícito solo en el consumer/outbox. Esquema de propiedades fijo: `TraceId`, `SpanId`, `MessageId`, `AggregateId`.
- `ActivitySource` compartido `"HotelBookingHub"`.

### Enforcement

- **`.editorconfig` + analyzers + `TreatWarningsAsErrors`** (incluye `CA2016`); `dotnet format` en pre-commit y CI; gitleaks/SAST (higiene de CI).
- **Check de CI sobre la lista cerrada de sufijos:** un test/step lee los sufijos permitidos de `AGENTS.md` y falla si un tipo público usa otro → la doc no puede desincronizarse en silencio.
- Una violación de patrón se documenta como hallazgo en el PR.

### Fuente canónica de reglas — `AGENTS.md` *(entregable derivado)*

Se materializa un **`AGENTS.md`** en la raíz: **imperativo** ("Usa X"), **tablas y listas cerradas**, ejemplo ✅/❌ pegado a cada regla, **anti-patterns como sección propia**, sin justificación inline (el *por qué* vive en `architecture.md`, que lo enlaza). Versionado junto al código y **referenciado desde el contexto de la herramienta de IA** (system prompt / `CLAUDE.md`) para que entre al contexto de cada tarea. Doble valor: es también artefacto del "uso de IA" que la prueba pide documentar.

### Anti-Patterns (evitar)

- Parsear `SqlException.Message` para clasificar errores.
- Exponer `Seq` (bigint interno) en DTOs/eventos/logs.
- Envolver respuestas en `{data,...}` o devolver 200 con error en el body.
- Regenerar el `MessageId` dentro del handler o del loop de retry.
- Endpoints con `IResult` desnudo y mapeo manual `Result`→HTTP por endpoint.
- `traceId` como `Guid` propio en vez del `TraceId` de `Activity`.
- Lógica de negocio en `Comun` o en el Gateway; garantizar el invariante en la aplicación en vez del motor.

### Alcance: qué corre vs qué se documenta

- **Con código que corre:** naming, `Result<T>`/Problem Details, anti-overbooking, ciclo de vida/cancelación, eventos + outbox + `Notificaciones.Worker` + SMTP, observabilidad OTel.
- **Documentado, no implementado (honesto):** read model MongoDB (ADR-013), waitlist (gancho), compensación por deshabilitación con reservas activas, nube Azure/Terraform (F3, con compuerta).

## Project Structure & Boundaries

### Árbol de proyecto completo

```
hotel-booking-hub/
├── HotelBookingHub.sln
├── Directory.Build.props            # net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors (+CA2016)
├── Directory.Packages.props         # Central Package Management
├── .editorconfig · .gitignore · .dockerignore
├── AGENTS.md                        # reglas canónicas (incluye "Comun no contiene tipos de dominio")
├── README.md                        # C4 + árbol comentado + enlaces a ADRs
├── .github/workflows/ci.yml         # build · unit · integration · contracts · dotnet format · gitleaks/SAST · newman · smoke compose · check sufijos
├── src/
│   ├── AppHost/{AppHost, ServiceDefaults}       # Aspire: recursos + OTel/health/resiliencia
│   ├── ApiGateway/                              # YARP (dotnet new web) — auth JWT, rate limit, HTTPS
│   ├── Comun/HotelBookingHub.Comun/             # shared kernel — SOLO contratos transversales
│   │   ├── Resultados/ · Mediador/ · Behaviors/ · Eventos/ · Errores/ · Primitivos/
│   └── Servicios/
│       ├── Hoteles/{Hoteles.Domain, .Application, .Infrastructure, .Api}
│       └── Reservas/
│           ├── Reservas.Domain/     # Reservas/ · Cancelaciones/ · Servicios/ (CalculadorPrecio, PoliticaCancelacion) · Puertos/
│           ├── Reservas.Application/ # organizado por SLICE (caso de uso), no por tipo técnico:
│           │   ├── Reservas/CrearReserva/            # Command+Handler+Validator juntos (CAP-5)
│           │   ├── Reservas/BuscarDisponibilidad/    # Query+Handler (CAP-4)
│           │   ├── ReservasAgente/ObtenerReservas/   # Query (CAP-3)
│           │   └── Cancelaciones/{SolicitarCancelacion, ResolverCancelacion}/  # CAP-10/11
│           ├── Reservas.Infrastructure/ # Persistencia/ · Migraciones/ · Outbox/ · Proyeccion/ · Idempotencia/
│           └── Reservas.Api/          # Minimal API /api/v1/reservas (+ /cancelaciones)
│       └── Notificaciones/Notificaciones.Worker/     # consume eventos → SMTP (idempotente)
├── tests/
│   ├── TestKit/                     # class lib: Fixtures (SqlServer/Redis/Dapr, imagen pineada), Builders, Collections
│   ├── Hoteles.UnitTests/  · Hoteles.IntegrationTests/
│   ├── Reservas.UnitTests/          # CalculadorPrecio, PoliticaCancelacion, ciclo de vida, behaviors, clasificación 2627/1205
│   ├── Reservas.IntegrationTests/   # G1 concurrencia + Resilience/OutboxFaultInjection (collections aisladas)
│   ├── Contracts/                   # esquema de eventos Reservas↔Worker (JSON Schema/DTO snapshot, sin containers)
│   └── E2E/                         # flujo completo crear→confirmar→notificar (compose; stage separado)
├── deploy/{docker-compose.yml, dapr/, terraform/}
├── postman/                         # colección + entorno (Newman)
└── docs/                            # DOCUMENTO-BASE, specs/, planning-artifacts/ (este architecture.md), adr/
```

### Justificación de cada frontera (una frase por assembly — "existe porque…")

- **`.Domain`** — aísla las reglas e invariantes del dominio de todo I/O; permite testear `CalculadorPrecio`/`PoliticaCancelacion`/ciclo de vida sin BD (sostiene el TDD del flujo crítico).
- **`.Application`** — orquesta casos de uso (commands/queries + behaviors) sin conocer detalles de persistencia; organizada por *slice* para que un cambio de caso de uso toque una carpeta, no cuatro.
- **`.Infrastructure`** — concentra los adaptadores (EF Core, Outbox, Redis, Dapr) detrás de los puertos del dominio; la frontera de **compilación** impide que el dominio dependa de EF Core aunque alguien lo intente.
- **`.Api`** — expone HTTP (Minimal API) y traduce `Result<T>`→HTTP; sin lógica de negocio.
- **`Comun`** — unifica las convenciones *transversales* (Result, mediador, behaviors, envelope, ProblemDetails) para que los servicios no diverjan en cómo se comunican.

> La frontera de ensamblado (no de carpeta) se elige a propósito: en una prueba evaluada por claridad, el `.sln` **es** la evidencia visible de Clean Architecture. La disciplina *interna* de cada capa se refuerza además con **NetArchTest** en `*.UnitTests` (p. ej. "Domain no referencia Microsoft.EntityFrameworkCore").

### Regla de admisión de `Comun`

Un tipo entra a `Comun` **solo si** (1) es una convención de infraestructura transversal (*cómo* se comunican los servicios, no *de qué* hablan) y (2) cambiar su forma requeriría coordinar ambos BCs de todas formas. **Ningún tipo de dominio** de Hoteles o Reservas va en `Comun`, aunque sea cómodo — se prefiere duplicación deliberada al acoplamiento oculto. (Regla replicada en `AGENTS.md`.)

### Architectural Boundaries

- **API (borde externo):** solo el Gateway expone `/api/v1/*` (auth JWT + rate limit + HTTPS); los servicios no se exponen directamente. Cada servicio publica OpenAPI + Scalar.
- **Servicio (BC):** Hoteles y Reservas son procesos independientes, **una BD por servicio**, sin llamadas síncronas — solo eventos Dapr pub/sub. `Comun` se comparte como biblioteca de contratos, nunca como estado.
- **Datos:** invariante anti-overbooking **local a la BD de Reservas** (slots + índice único). Hoteles dueño del catálogo; Reservas mantiene `ProyeccionHabitacion` por eventos. `Seq` (bigint) nunca cruza el BC.
- **Capas:** `Domain ← Application ← Infrastructure ← Api` (dependencias hacia adentro).

### Estrategia de tests (aislamiento y determinismo)

- **Tipos:** `*.UnitTests` (xUnit + InMemory/puro + NetArchTest) · `*.IntegrationTests` (Testcontainers.MsSql) · `Contracts` (esquema de eventos, sin containers, rápido, en cada PR) · `E2E` (compose completo, stage separado, bloqueante solo en PR a `main`).
- **`TestKit`** centraliza fixtures (imagen de contenedor **pineada**), data builders y `ICollectionFixture` — sin duplicar setup entre servicios.
- **Aislamiento crítico (no negociable):** G1 (concurrencia) y `OutboxFaultInjection` viven en **collections xUnit separadas con `DisableParallelization = true`**, cada una con su propio contenedor; reset de estado por test (`Respawn` o tx+rollback); el "broker caído" se inyecta como **fake controlable de Dapr**, no tumbando infra real. En CI corren como **stage secuencial aparte**, nunca en el `dotnet test` masivo paralelo.

### Requirements → Structure Mapping

| Capacidad / FR | Ubicación |
|---|---|
| CAP-1/2 · FR-1…7 | `Servicios/Hoteles/*` |
| CAP-3 · FR-13 | `Reservas.Application/ReservasAgente/ObtenerReservas` |
| CAP-4 · FR-8 | `Reservas.Application/Reservas/BuscarDisponibilidad` + `Infrastructure/Proyeccion` + Redis |
| CAP-5 · FR-9…12 | `Reservas.Application/Reservas/CrearReserva` + `Domain/Servicios/CalculadorPrecio` |
| CAP-6 · FR-18 | `Reservas.Infrastructure/Persistencia` (slots + índice único) |
| CAP-10/11 · FR-14…17 | `Reservas/{Domain,Application}/Cancelaciones/` |
| CAP-7 · FR-19…21 | `Reservas.Infrastructure/Outbox` → `Notificaciones.Worker` |
| CAP-8 · FR-22…24 | `ApiGateway` + policies por servicio |
| CAP-9 · FR-25/26 | `AppHost/ServiceDefaults` (OTel) |

### Integration Points & Data Flow

- **Reserva:** `POST /api/v1/reservas` → Gateway → `Reservas.Api` → `CrearReserva` (tx: `Reserva` + `NochesHabitacion` + `OutboxMessages`) → relay publica `ReservaConfirmada` → `Notificaciones.Worker` (idempotente) → SMTP.
- **Catálogo→disponibilidad:** eventos de Hoteles → `ProyeccionHabitacion` (idempotente/ordenada) → alimenta la búsqueda.
- **Cancelación:** `POST /reservas/{id}/cancelaciones` + `PATCH .../{cid}` → eventos por outbox → Worker.
- **Externo:** SMTP y, en F3, Azure (Service Bus/SQL/Redis/Key Vault) vía componentes Dapr, sin cambio de código.

## Architecture Validation Results

### Coherence Validation ✅

Stack coherente y con versiones verificadas (.NET 10 LTS, Aspire 13, EF Core 10, YARP, Dapr, Redis); sin contradicciones tras la sincronización (índice único ADR-016 reemplaza a `SERIALIZABLE` en todo el contrato; claves ADR-017 coherentes con EF Core y con el envelope de eventos). Patrones (naming, formato, envelope, error→HTTP, pipeline) alineados con las decisiones; `AGENTS.md` + check de sufijos en CI evitan desincronización. Estructura (4 assemblies/BC + slices + fronteras de datos) soporta Clean Architecture, CQRS y el invariante local.

### Requirements Coverage Validation ✅

11 CAP y 26 FR mapeados a ubicación concreta; 8 NFR con mecanismo asignado (ver "Requirements → Structure Mapping" y "Core Architectural Decisions"). Sin FR huérfano.

### Contrato del mediator — ADR-018 (cierre del último bloqueo de implementación)

- **Firma:** `Task<TResponse> Handle(TRequest request, CancellationToken ct)` en `IRequestHandler<TRequest, TResponse>`, donde `TResponse` es `Result` o `Result<T>` (los flujos esperados no lanzan; 409/400/403 son `Result`).
- **Pipeline por decorators** `IPipelineBehavior<TRequest,TResponse>` (composición anidada, no hardcode), orden `Logging → Validation → Transaction → Outbox → Handler`.
- **Registro por scan de assembly** en un único `AddMediatorPipeline()` por servicio, behaviors en orden explícito.
- **Regla no negociable (atomicidad):** el insert a `OutboxMessages` va en el **mismo `DbContext.SaveChangesAsync()`** que el cambio de dominio; el `TransactionBehavior` envuelve ambos y asigna el `MessageId` una vez antes del retry 1205.
- **Pendiente de dibujo:** diagrama de secuencia de una escritura en `architecture-diagrams.md`.

### Idempotencia del consumidor (explícita, no implícita)

El outbox es **at-least-once por diseño**; la no-duplicación se garantiza **en el consumidor**, no en el wire: `Notificaciones.Worker` y el handler de `ProyeccionHabitacion` deduplican por **(`MessageId`, `version`)** (inbox Redis SETNX+TTL) y descartan eventos fuera de orden. Sostiene G3 y protege G7/G1 en cascada.

### Alternativas rechazadas (con costo evitado)

| Alternativa | Elegido | Costo que se evita |
|---|---|---|
| `SERIALIZABLE` | Índice único + READ COMMITTED (ADR-016) | Deadlocks bajo N alto → degradan G7 |
| Dapr outbox nativo | Outbox manual | Acoplar la persistencia al state store de Dapr, perder el aggregate rico |
| `aspire-starter` | AppHost+ServiceDefaults a medida (ADR-015) | Código muerto de muestra que el evaluador debe descartar |
| UUID v7 clustered naive | v7 no-clustered + `Seq` bigint (ADR-017) | Fragmentación del índice clustered |
| Read model MongoDB ahora | Redis + proyección (ADR-013) | Una BD más que asegurar/sincronizar/desplegar |
| 1 proyecto por servicio | 4 assemblies por BC | Perder la evidencia visible de Clean Architecture |
| MediatR comercial | Mediator propio (ADR-005/018) | Dependencia con licencia comercial |

### Gap Analysis

- **Críticos:** ninguno abierto (los tres forks + el contrato del mediator quedaron resueltos).
- **Importantes (acciones de implementación):** materializar `AGENTS.md`, `README` (enrutador + C4 + árbol comentado), `.editorconfig`/analyzers, ADR-015/016/017/018 en `docs/adr/`, y el diagrama de secuencia de escritura.
- **Menores / futuro:** compensación por deshabilitación; read model Mongo; waitlist; nube Azure (F3).

### Riesgo residual (honesto — dónde el diseño tiene cicatriz)

- **Ventana de re-entrega del outbox** (at-least-once): no se elimina, se **absorbe** con dedupe en el consumidor; si ese dedupe falla, se duplica el efecto. Punto más delicado.
- **Staleness de `ProyeccionHabitacion`** (consistencia eventual): la búsqueda es best-effort; el invariante duro sigue en el motor → a lo sumo produce 409 evitables, nunca overbooking.
- **Mediator propio:** sin el ecosistema de MediatR; el wiring hay que escribirlo y **testearlo** (ADR-018 + test de pipeline) — a cambio de cero dependencia con licencia.

### Architecture Completeness Checklist

Requirements Analysis — [x] contexto · [x] escala/complejidad · [x] restricciones · [x] cross-cutting.
Architectural Decisions — [x] críticas con versiones · [x] stack · [x] integración · [x] rendimiento.
Implementation Patterns — [x] naming · [x] estructura · [x] comunicación · [x] proceso.
Project Structure — [x] directorios · [x] fronteras · [x] integración · [x] mapeo requisito→estructura.

### Architecture Readiness Assessment (dos ejes)

- **Diseño:** COMPLETO — 16/16, 0 gaps conocidos, contrato del mediator cerrado. **Confianza: alta** (coherencia verificable por inspección + ADRs).
- **Ejecución:** **NO VALIDADA** — los gates de runtime (G1, G3, G7) no tienen aún una línea de test corriendo. **Confianza: pendiente**, condicionada a dos spikes de **Sprint 0**:
  1. **Money test G1×G3×proyección:** 50-100 requests concurrentes sobre la misma disponibilidad + matar el broker a mitad de ráfaga + **fault-injection de deadlock 1205 a mitad de una reserva multi-noche** → verificar de una corrida: exactamente 1 confirmada + resto 409, rollback **todo-o-nada a nivel de reserva** (no por noche), **exactamente un evento de outbox coherente** (cero huérfanos, cero reservas parciales), 0 pérdida/duplicado tras recuperar, proyección converge sin eventos fuera de orden.
  2. **Wiring del mediator** (ADR-018): el pipeline compone en orden y outbox+dominio comparten `SaveChanges`.

**Criterio de aborto / Plan B por spike (riesgo acotado):** si READ COMMITTED + retry no da la garantía (o el spike no cierra en su timebox), caer a `SERIALIZABLE` **sin** retry, aceptar menor concurrencia y documentarlo como riesgo conocido (revierte parcialmente ADR-016, ya trazado). Decidido de antemano → un spike que sale mal es aprendizaje acotado, no bloqueo de cronograma.

**Overall:** LISTO PARA IMPLEMENTAR (diseño), CON VALIDACIÓN DE EJECUCIÓN PENDIENTE (spikes de Sprint 0).

### Fortalezas (en jerarquía)

1. **Diferenciador real:** consistencia **asimétrica nombrada** + **invariante garantizado por el motor** con criterio de aislamiento justificado. Requiere entender el dominio; no lo genera una plantilla.
2. **Sostén:** outbox honesto (garantía en el efecto, no en el wire); cada frontera de assembly con su justificación; decisiones estresadas en 6 rondas de party-mode y verificadas por web en los puntos sensibles.
3. **Higiene (esperable):** trazabilidad FR↔CAP↔ADR↔ubicación; alcance honesto (qué corre vs qué se documenta).

### Implementation Handoff

**Guía para agentes:** seguir decisiones y patrones exactamente; `AGENTS.md` (el qué) + este `architecture.md` (el porqué); respetar fronteras de assembly y la regla de admisión de `Comun`.
**Artefactos imprescindibles de la entrega:** diagrama de secuencia de la escritura crítica (reserva+slots+outbox en una tx) y `README`-enrutador (tabla "si quieres saber X → ve a Y", sin duplicar); C4 de contenedores como segundo.
**Primera prioridad (Sprint 0):** inicialización (solución + CPM + Aspire + estructura) → **los dos spikes de validación de ejecución** → luego el core con TDD (`CalculadorPrecio` → anti-overbooking con Testcontainers.MsSql).
