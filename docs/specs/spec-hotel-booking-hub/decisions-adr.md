# Registro de decisiones (ADRs)

Compañero de [SPEC.md](SPEC.md). Formato corto: Contexto · Decisión · Consecuencias. En el repo se expanden en `docs/adr/`.

### ADR-001 — Arquitectura de microservicios por Bounded Context
- **Contexto:** el enunciado premia separar ≥2 dominios con contratos y trade-offs.
- **Decisión:** 2 microservicios (Hoteles, Reservas) + Gateway + Worker, comunicados por eventos.
- **Consecuencias:** (+) despliegue/escala independiente, límites claros. (−) complejidad operativa y consistencia eventual (mitigada con proyecciones + outbox).

### ADR-002 — Dapr como runtime de pub/sub y secretos
- **Decisión:** Dapr para pub/sub + secrets; broker RabbitMQ local / Service Bus nube por component.
- **Consecuencias:** (+) cloud-agnostic, nativo en ACA, cero cambios de código al cambiar broker. (−) sidecars añaden piezas al compose (mitigado por Aspire en dev y ACA gestionado en nube).

### ADR-003 — SQL Server con anti-overbooking por slots de inventario
- **Contexto:** SQL Server es el motor de UltraGroup; el invariante exige garantías fuertes bajo concurrencia; SQL Server no tiene *exclusion constraints*.
- **Decisión:** tabla de slots `NochesHabitacion` con clave única `(HabitacionId, Noche)`; el conflicto se arbitra en el propio INSERT (el nivel de aislamiento se decide en ADR-016).
- **Consecuencias:** (+) cero overbooking garantizado por el motor, portable, soporta disponibilidad parcial. (−) N filas por reserva (irrelevante); menos escalado horizontal de escritura que NoSQL (no necesario a esta escala).

### ADR-004 — Transactional Outbox + idempotencia
- **Decisión:** Outbox en la misma tx que la reserva; inbox por `message-id` en Redis; DLQ + retries.
- **Consecuencias:** (+) entrega *at-least-once* sin pérdida + procesamiento *effectively-once*. (−) tabla outbox y relay adicionales.

### ADR-005 — CQRS con mediator propio
- **Contexto:** MediatR pasó a licencia comercial; se busca repo público limpio.
- **Decisión:** mediator propio minimalista con pipeline de behaviors; Wolverine descartado por solaparse con Dapr.
- **Consecuencias:** (+) cero dependencia de licencia, demuestra dominio del patrón. (−) mantener ~30 líneas propias.

### ADR-006 — JWT propio OIDC + RBAC (también en nube)
- **Decisión:** emisor JWT propio (OIDC simple) + RBAC server-side, **en local y en nube**; Entra ID descartado para mantener reproducibilidad y control end-to-end.
- **Consecuencias:** (+) reproducible y fácil de evaluar; sin dependencia de un IdP gestionado. (−) no es un IdP completo (aceptable al alcance); si se requiriera federación empresarial, habría que introducir Entra ID más adelante.

### ADR-007 — Aspire para desarrollo + docker-compose mantenido a mano
- **Decisión:** Aspire como fuente de verdad de la topología (dev + OTel); el `docker-compose` se **mantiene a mano** (no se genera desde `Aspire.Hosting.Docker`) y se blinda con un smoke test en CI (`docker compose up` + verificación de `/health`) que detecta drift.
- **Consecuencias:** (+) control total del compose (incluidos sidecars Dapr) + entrega reproducible + observabilidad. (−) dos representaciones a mantener en sincronía (mitigado con el smoke test en CI).

### ADR-008 — Azure Container Apps + Terraform (con criterio de migración a AKS)
- **Contexto:** dejar la app lista para nube y demostrar IaC. Se evaluó ACA vs AKS.
- **Decisión:** desplegar en ACA (Dapr y KEDA gestionados) + Azure SQL + Azure Cache for Redis, **exclusivamente por Terraform** (sin provisión manual ni click-ops); Fase 3 con compuerta. El entregable de nube es la IaC ejecutable. ACA corre sobre AKS por debajo.
- **Cuándo migraría a AKS (documentado, no ejecutado):** control fino (ingress controllers, network policies, service mesh), workloads no-serverless, multi-cloud. La migración es viable porque Dapr y los contenedores son los mismos.
- **Consecuencias:** (+) despliegue real y escalable sin operar K8s; demuestra criterio ACA↔AKS. (−) menos control fino que AKS (se documenta el camino de salida).

### ADR-009 — Sin dependencias privadas
- **Decisión:** adoptar los *patrones* de la base BMAD (Clean Architecture, DDD, Result, UUID v7, DateTimeOffset, Minimal API) con código propio/OSS; no incluir MasterPattern/AccessManager/LookupField (GitHub Packages privados).
- **Consecuencias:** (+) repo 100% reproducible. (−) reimplementar patrones base (aporta control).

### ADR-010 — Resiliencia selectiva
- **Decisión:** Polly/Http.Resilience en email y llamadas entre servicios; no en todos los métodos.
- **Consecuencias:** (+) resiliencia donde importa, sin sobre-ingeniería.

### ADR-011 — OpenAPI como contrato, Scalar como UI
- **Decisión:** generar el spec OpenAPI (cumple el requisito) y exponer Scalar como UI en vez de Swagger UI.
- **Consecuencias:** (+) contrato estándar + UI moderna. (−) el evaluador podría esperar Swagger UI (se aclara en README que el spec está en `/openapi/v1.json`).

### ADR-012 — Redis para caché, idempotencia y state
- **Decisión:** Redis como caché de disponibilidad, inbox de idempotencia (message-id con TTL) y Dapr state store.
- **Consecuencias:** (+) lecturas rápidas, idempotencia simple, alineado al stack. (−) una dependencia de infra más (gestionada por Aspire/compose/ACA).

### ADR-013 — Read model CQRS en MongoDB (diferido)
- **Decisión:** diseñar y documentar el read model en MongoDB (proyección desnormalizada por eventos, con seguridad SCRAM/RBAC/TLS/cifrado en reposo + CSFLE para PII), pero **no implementarlo** en esta entrega. Redis cubre el caché mientras tanto.
- **Consecuencias:** (+) demuestra dominio de CQRS y seguridad de datos sin añadir una BD más que asegurar/sincronizar/desplegar. (−) la separación write/read queda como evolución, no como código entregado.

### ADR-014 — Cancelación de reservas: política default + discreción del agente
- **Contexto:** regla de negocio **propia** (no exigida por el enunciado), refinada con el equipo en party-mode. Racional: una noche de hotel es **inventario perecedero** — la que no se vende, se pierde. El anti-overbooking protege contra la sobreventa; la cancelación gestionada protege contra el hueco no revendible de una cancelación tardía. Narrativa de diseño: *el motor no negocia el overbooking (determinismo); la cancelación es donde se introduce criterio humano a propósito (juicio)*.
- **Decisión:** política de penalización como **default sugerido, no impuesto** (≥30 días al check-in → 0%, <30 días → 100%; ref = fecha de solicitud, `PenalidadSugerida` congelada). El **agente resuelve con discreción**: aprobar aplicando, aprobar condonando, o **rechazar** (la reserva vuelve a `Confirmada`). Dos vías de inicio (viajero por el sistema / agente en su nombre) con **un solo comando** + VO `Iniciador` (autorización en el borde). **Atajo** de un paso para el agente, con ambos eventos auditados. Ciclo de vida `Confirmada → CancelacionSolicitada → {Cancelada | Confirmada}` por **enum + guards**. Los slots se liberan **solo al aprobar**; el rechazo es igualmente transaccional. Todo por **outbox** (`SolicitudCancelacionRegistrada` / `ReservaCancelada` / `SolicitudCancelacionRechazada`). **Sin pagos reales**: penalidad como monto adeudado. **Auditoría:** motivo (categoría + texto libre), origen, quién decidió, flag default/override, motivo de rechazo.
- **Alternativas / límites:** penalidad automática rígida (descartada: castiga sin recuperar valor y quema la relación); tramos escalonados de penalidad (futuro — hoy los cubre la discreción manual); **SLA / expiración** de solicitudes pendientes (fuera de alcance — se expone la antigüedad "días en espera"); **waitlist** al "segundo interesado" (futuro — `ReservaCancelada` ya se enriquece con hotel/tipo/fechas como gancho); estado formal de "disputa" (descartado por alcance).
- **Consecuencias:** (+) modela un dominio real (default + override auditado) que demuestra criterio de diseño —dónde el dominio exige regla dura vs blanda—; reusa outbox/idempotencia; extensible a waitlist sin tocar el agregado. (−) amplía el core de Fase 1 y añade estados/campos de auditoría; la **modificación/reprogramación** de una reserva sigue fuera de alcance (se resuelve cancelando y volviendo a reservar).

### ADR-015 — Arranque sin `aspire-starter`: AppHost + ServiceDefaults a medida
- **Contexto:** la plantilla `aspire-starter` scaffoldea una app de muestra (Blazor Web + API + tests) que no aplica a un back end puro con estructura DDD a medida.
- **Decisión:** partir de una solución vacía + `aspire-apphost` + `aspire-servicedefaults` (Aspire 13, NuGet-only vía `Aspire.AppHost.Sdk`), y añadir los servicios de dominio a mano según la estructura del contrato; gobernanza de versiones con Central Package Management (`Directory.Packages.props`) y `Directory.Build.props` desde el primer commit. El Gateway se crea con `dotnet new web` (no `webapi`).
- **Consecuencias:** (+) cero código muerto que el evaluador deba descartar; orquestación + OpenTelemetry reproducibles; estructura folder-per-bounded-context legible. (−) el `Program.cs` de cada servicio se escribe a mano en vez de heredarlo del template.

### ADR-016 — Arbitraje del invariante por índice único (READ COMMITTED) en vez de SERIALIZABLE
- **Contexto:** ADR-003 fijó inicialmente `SERIALIZABLE` para insertar los slots. Al diseñar la arquitectura (`bmad-create-architecture`) se reconsideró: el `UNIQUE (HabitacionId, Noche)` ya captura el conflicto en el propio INSERT, sin necesidad de prevenir *phantom reads*.
- **Decisión:** el INSERT de los N slots corre bajo **READ COMMITTED**; la violación de unicidad (`SqlException.Number` **2627/2601**) es el árbitro → **409 inmediato, sin retry** (determinístico: otro ganó). Solo el **deadlock 1205** se reintenta (3 intentos, backoff+jitter); el retry re-ejecuta el handler completo (idempotente, outbox en la misma tx). Los slots del batch se insertan en **orden determinístico** (`ORDER BY HabitacionId, Noche`) para minimizar deadlocks. La reserva multi-slot es **todo-o-nada** por la transacción. La clasificación del error es por `SqlException.Number`, **nunca** por el mensaje.
- **Alternativas:** `SERIALIZABLE` (descartado: más caro y más deadlocks bajo contención → afecta el p95/p99 de búsqueda de G7, sin beneficio dado que el índice único ya arbitra); `sp_getapplock` (descartado por complejidad).
- **Consecuencias:** (+) menos contención y locks, protege G7; la garantía sigue en el motor. (−) exige clasificar el número de error SQL con precisión. Supuesto: `Habitacion` es unidad física individual (no categoría con cupo N); si evolucionara a inventario por tipo, el árbitro cambia (contador + concurrencia optimista).

### ADR-017 — Estrategia de claves: UUID v7 como identidad + clustering key secuencial
- **Contexto:** el stack fija UUID v7 en las PKs. `Guid.CreateVersion7()` de .NET no produce el orden secuencial que el tipo `uniqueidentifier` de SQL Server ordena, por lo que como PK *clustered* fragmenta el índice casi como un v4 (trap documentado).
- **Decisión:** la identidad de dominio es el **UUID v7** (expuesto en API y eventos), mapeado como PK **no-clustered**; la *clustering key* es una columna secuencial interna **`Seq bigint IDENTITY`**. `Seq` es *shadow property*: nunca cruza la frontera del BC ni aparece en DTOs/eventos/logs; las FK apuntan al Guid. `NochesHabitacion` no usa surrogate — su clustering key natural es `(HabitacionId, Noche)`, que además es el índice árbitro.
- **Alternativas:** v7 *clustered* naive (descartado: fragmentación); reordenar los bytes del v7 al layout de SQL Server (descartado: el valor almacenado deja de ser el v7 canónico salvo conversión); `SequentialGuidValueGenerator` de EF Core (descartado: no es v7).
- **Consecuencias:** (+) identidad pública estable y secuencialidad física a cualquier escala. (−) una columna extra por tabla con surrogate; disciplina para no filtrar `Seq`. A la escala objetivo (~0,12 writes/s) la fragmentación sería tolerable, pero la decisión cuesta poco y evita el trap si el volumen crece.

### ADR-018 — Contrato del mediator propio y atomicidad del outbox
- **Contexto:** el mediator propio (ADR-005) estaba especificado por tamaño ("~30 líneas") pero no por contrato; al validar la arquitectura se detectó que era el único bloqueo real de implementación (la firma la hereda cada handler; la composición decide la atomicidad del outbox).
- **Decisión:** `IRequestHandler<TRequest, TResponse>` con firma `Task<TResponse> Handle(TRequest request, CancellationToken ct)`, donde `TResponse` es `Result`/`Result<T>` (los flujos esperados no lanzan; 400/403/409 son `Result`). Pipeline por **decorators** `IPipelineBehavior<TRequest,TResponse>` (composición anidada), orden `Logging → Validation → Transaction → Outbox → Handler`; registro por **scan de assembly** en un único `AddMediatorPipeline()` por servicio. **Regla no negociable:** el insert a `OutboxMessages` ocurre en el **mismo `DbContext.SaveChangesAsync()`** que el cambio de dominio; el `TransactionBehavior` envuelve ambos y asigna el `MessageId` una sola vez antes del retry 1205 (que es todo-o-nada a nivel de reserva, no por noche).
- **Alternativas:** pipeline hardcodeado sin decorators (descartado: no extensible, cada handler repite cross-cutting); MediatR comercial (descartado por licencia, ADR-005); dos `SaveChanges` separados para dominio y outbox (descartado: rompe la atomicidad del patrón outbox).
- **Consecuencias:** (+) contrato uniforme, cross-cutting en un solo lugar, atomicidad garantizada. (−) hay que escribir y **testear** el wiring (test de pipeline + verificación de que outbox y dominio comparten transacción); se valida en el spike de Sprint 0.

### ADR-019 — Transporte de eventos por Strategy según entorno (RabbitMQ local / Dapr nube)
- **Contexto:** ADR-002 fijó "Dapr para pub/sub" con la promesa "cambiar de broker = solo YAML". Ese transporte se difirió desde E1/E5 y nunca se cableó: el único adaptador de `IPublicadorEventos` era `PublicadorEventosLog` (placeholder) → los eventos no cruzaban ningún broker en runtime. Detectado al cerrar E7; decidido en party-mode (John+Winston+Amelia) + Santiago (2026-07-10). Refina —no reemplaza— ADR-002.
- **Decisión:** el transporte se selecciona por **entorno** detrás del puerto hexagonal `IPublicadorEventos` (patrón Strategy; el dominio nunca conoce el transporte). **LOCAL/`docker-compose`:** adaptador **RabbitMQ directo** (`RabbitMQ.Client`) — se cablea en la **Épica 9** (Story 9.1), con consumidor `BackgroundService` en el worker que reusa `DespachadorNotificaciones` (inbox idempotente + dead-letter); los eventos corren end-to-end y se verifican con Testcontainers dentro de `dotnet test`. **NUBE:** adaptador **Dapr pub/sub → Azure Service Bus** — vive en la **Épica 8** (ACA con Dapr gestionado), donde la promesa "solo YAML del component" aplica **dentro** de Dapr (RabbitMQ↔Service Bus).
- **Alternativas:** (A) cablear Dapr también en local (descartado ahora: sidecars + Dapr CLI + doble puerto + E2E fuera de `dotnet test` → esfuerzo L en la fase de mayor riesgo, sin cobrar el beneficio cloud si E8 se recorta); (C) solo documentar la brecha (descartado por los 3 agentes: se está a un adaptador de que funcione end-to-end).
- **Consecuencias:** (+) el diferenciador de mensajería **corre** en local (no solo documentado); portabilidad de dominio preservada (puerto limpio); testeable en verde. (−) el salto local→nube **no** es "solo YAML" a nivel de app: selecciona un adaptador distinto por entorno (matiza AC-E8.1.2, que aplica dentro del adaptador Dapr); dos adaptadores de transporte a mantener. La opción de añadir `PublicadorEventosDapr` sin tocar el dominio queda comprada por el diseño hexagonal.

### ADR-020 — Gestión de secretos por entorno (env vars local / Dapr Secrets + Key Vault nube)
- **Contexto:** ADR-002 y NFR-4 decían "cero secretos en repo **vía Dapr Secrets (local) / Key Vault (nube)**". Como Dapr no se cableó (ADR-019), en local los secretos se gestionaron con **variables de entorno** (`MSSQL_SA_PASSWORD`, `JWT_SIGNING_KEY` desde `deploy/.env` gitignored) + gitleaks en CI. El outcome "cero secretos en repo" (G6/NFR-4) **se cumple**; lo que divergió es el mecanismo. Decidido en party-mode + Santiago (2026-07-10).
- **Decisión:** **LOCAL:** secretos por **variables de entorno** + `.env` (en `.gitignore`) + gitleaks en CI — mecanismo válido y auditado; no se cablea Dapr Secrets ni Key Vault en local (sería gold-plating para la prueba). **NUBE (E8):** el `secretKeyRef` de los components Dapr resuelve contra **Dapr Secrets → Azure Key Vault**, provisionado por Terraform. No es una regresión de seguridad: es el mismo objetivo por el mecanismo adecuado a cada entorno.
- **Alternativas:** implementar un secret store de Dapr local ahora (descartado: requiere cablear Dapr, fuera de alcance de E9; ver ADR-019).
- **Consecuencias:** (+) cero secretos en repo cumplido y verificable hoy; sin complejidad de Key Vault en local. (−) divergencia con la letra de ADR-002 (ahora registrada); el `secretKeyRef` de `deploy/dapr/*.yaml` solo cobra sentido al cablear Dapr en E8.

### ADR-021 — CD por OIDC federated + despliegue on-demand con aprobación (cero secretos)
- **Contexto:** al cruzar la compuerta de Fase 3 (E8, desplegar de verdad) hace falta un camino de aprovisionamiento hacia Azure. Un CD que auto-aplica en cada merge a `main` crearía infra **facturable** por accidente; y un Service Principal con secreto en GitHub violaría el invariante cero-secretos (ADR-020). Decidido en party-mode (Winston+John+Amelia) + Santiago (2026-07-10).
- **Decisión:** autenticación del CD por **OIDC federated credentials** (App Registration + federated credential; issuer `token.actions.githubusercontent.com`, subject `repo:SantiagoRenteria/hotel-booking-hub:environment:production`). En el repo solo variables **no secretas** (`AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID`). Disparo **on-demand** (`workflow_dispatch` para deploy y para destroy), **no** auto-apply en merge. Un GitHub **Environment `production` con required reviewer** exige aprobación humana antes del `apply` (el token OIDC, atado al environment, no se emite hasta la aprobación). `main` protegida (PR + required checks) — la regla la aplica Santiago con `gh` (el agente no modifica controles de acceso del repo, por política de seguridad).
- **Alternativas:** (A) Service Principal con secreto en GitHub Secrets (descartado: viola cero-secretos, el secreto caduca/rota/se filtra); (B) CD auto-apply en cada merge (descartado por John: quema dinero por accidente en una prueba apply→destroy; anti-patrón sin gate de costo); (C) deploy 100% manual local sin pipeline (descartado: no demuestra el dominio de CD/OIDC que la prueba quiere exhibir).
- **Consecuencias:** (+) despliegue auditado y passwordless; imposible gasto accidental por merge; el approval cose seguridad y auth en un solo tejido (OIDC ↔ Environment). (−) no es "CD 100% automático" (un clic humano antes del apply — aceptable y correcto para infra facturable).
- **Refinamiento y reversión (Story 8.3 → T.2):** durante la 8.3 se probó un refinamiento que auto-aplicaba al **merge a `main`** (`cd.yml` con trigger `push: main`), dejando el `workflow_dispatch` solo para destroy. En T.2 (2026-07-11) **Santiago revirtió** ese cambio: el trigger `push: main` se eliminó y el despliegue vuelve a ser **on-demand** (`workflow_dispatch` → `accion=deploy`/`destroy`), tal como fija la Decisión de arriba. Motivo de la reversión: el auto-apply reintroducía el gasto accidental por merge que el ADR busca evitar; mergear a `main` ahora **solo** protege/sincroniza la rama y **no** despliega. Se conservan: `main` protegida (PR + 1 review + checks), `environment: production` como gate opcional de required reviewers, OIDC/cero-secretos. La app registration y la branch protection las ejecuta Santiago (comandos en `deploy/terraform/README.md`).

### ADR-022 — State remoto de Terraform por bootstrap `az` + backend AAD + dos resource groups
- **Contexto:** el CD y el ciclo apply→destroy repetido necesitan un `tfstate` compartido y con lock; pero el Storage Account del state no puede vivir en su propio state (huevo-gallina), y un state local subido como artifact es frágil (sin lock, carreras, artifact perdido = recursos huérfanos costando). Decidido en party-mode + Santiago (2026-07-10).
- **Decisión:** un **script `bootstrap` idempotente** (`az group/storage account/container create`) crea el backend **una vez**, fuera del ciclo destroy. Backend `azurerm` con **`use_azuread_auth=true`** (cero claves de storage; auth por la sesión `az`/OIDC, rol *Storage Blob Data Contributor*). **Dos resource groups:** **RG-state permanente** (tfstate, nunca se destruye, cuesta centavos) y **RG-app efímero** (todo lo demás, apply→destroy). El lock lo da el blob lease del backend.
- **Alternativas:** (A) state local + artifact (descartado: frágil con destroy repetido, sin lock); (B) módulo Terraform bootstrap con su propio state (descartado: reintroduce el mismo problema de state en un segundo proyecto).
- **Consecuencias:** (+) `terraform destroy` arrasa la app sin tocar el state; lock nativo; passwordless. (−) un paso de bootstrap manual previo (documentado, corre una vez).

### ADR-023 — Scale-to-zero selectivo: worker de Notificaciones `min=1`, resto a cero
- **Contexto:** la estrategia de mínimo costo pide `min_replicas=0` en las Container Apps. Pero un **consumidor de eventos** a 0 réplicas no procesa mensajes salvo que exista un **KEDA scaler `azure-servicebus`**, y KEDA en ACA no usa Managed Identity de forma estable para leer la cola → reintroduciría un **secreto de conexión** (choca con cero-secretos, ADR-020). Sin scaler, los mensajes se acumulan en Service Bus (no se pierden) pero nadie los procesa → backlog. Decidido en party-mode + Santiago (2026-07-10).
- **Decisión:** **scale-to-zero selectivo** — `gateway/hoteles/reservas` con `min_replicas=0` (scale-to-zero real); `notificaciones.worker` con **`min_replicas=1`** (cómputo ínfimo en Consumption) para garantizar el consumo end-to-end **sin reintroducir secretos**. El KEDA scaler `azure-servicebus` con workload identity queda como **deuda/hardening futuro** documentado.
- **Alternativas:** (A) worker a 0 + KEDA scaler con connection string (descartado ahora: reintroduce secreto, rompe el invariante; más complejidad de auth del scaler); (B) todo `min=1` (descartado: no demuestra scale-to-zero en las apps HTTP y cuesta más cómputo).
- **Consecuencias:** (+) eventos consumidos de verdad end-to-end; cero-secretos intacto; scale-to-zero demostrado en las 3 apps HTTP. (−) el worker no baja a cero (costo de cómputo mínimo); el "scale desde cero del consumidor" queda como deuda, no exhibido en esta prueba.
