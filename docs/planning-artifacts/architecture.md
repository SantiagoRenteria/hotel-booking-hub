---
stepsCompleted: [1, 2, 3, 4, 5]
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
