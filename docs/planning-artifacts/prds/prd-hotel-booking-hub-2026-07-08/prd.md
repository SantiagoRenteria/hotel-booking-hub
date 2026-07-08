---
title: "PRD — Sistema de Gestión y Reserva de Hoteles (hotel-booking-hub)"
status: final
created: 2026-07-08
updated: 2026-07-08
---

<!-- En construcción vía bmad-prd (Coaching path, Vision + Features). Derivado de SPEC-hotel-booking-hub. -->

# PRD — Sistema de Gestión y Reserva de Hoteles (`hotel-booking-hub`)

## 1. Visión

Hoy la agencia gestiona hoteles y reservas de forma manual, lo que produce inconsistencias de inventario, pérdida de comisiones por errores y sobreventa, y una mala experiencia para el viajero. `hotel-booking-hub` es el **back end de una plataforma de alojamiento** que le da a la agencia un sistema de registro fiable: los agentes administran su inventario de hoteles y habitaciones, los viajeros buscan y reservan con disponibilidad real, y **el sistema garantiza por diseño que nunca haya sobreventa** — con contratos de API claros, trazabilidad de extremo a extremo y una base mantenible sobre la que crecer.

El producto sirve a **una única agencia operadora** (single-tenant). Una plataforma SaaS multi-agencia queda explícitamente fuera de alcance.

### Principio rector

Este back end es, además, un **entregable de prueba técnica evaluada**, y su criterio de evaluación es explícito: *"no se espera perfección en todos los frentes: se valorará la claridad del razonamiento detrás de las decisiones tomadas."* Ese criterio ancla cada decisión de alcance de este PRD: **core impecable + pocos diferenciadores con profundidad real + documentación que justifica cada decisión (incluida la de no hacer algo)**. Cuando haya que recortar, se recorta amplitud, no calidad del núcleo.

## 2. Objetivos y métricas de éxito

Métricas ancladas a lo **demostrable en test / CI / demo** (no hay telemetría de producción en vivo).

| # | Objetivo | Métrica de éxito | Contramétrica |
|---|----------|------------------|----------------|
| **G1** | Cero overbooking bajo concurrencia | Test de estrés con **N solicitudes concurrentes aleatorias (rango 30–100, sorteado por corrida)** sobre la misma habitación/fechas → exactamente 1 confirmada, el resto 409; el N sorteado y la semilla se registran en la salida para reproducibilidad | **0 falsos 409**: no rechazar reservas legítimas de habitaciones distintas o fechas no solapadas |
| **G2** | Reproducibilidad de un comando | `docker compose up` levanta el sistema y pasa el smoke test `/health` **sin instalar SDK ni Aspire** | Arranque en frío en tiempo razonable (objetivo suave) |
| **G3** | Notificación sin pérdida ni duplicado | Tras caída del broker, 100% de eventos entregados al reponerse; evento repetido → 0 correos duplicados | Sin sobre-resiliencia: retries/DLQ solo donde importa |
| **G4** | Contratos y trazabilidad | OpenAPI válido por servicio; `trace-id` propagado Gateway→servicio→sidecar→worker, visible en el dashboard | — |
| **G5** | Calidad verificable | Cobertura ≥80% en código nuevo; TDD evidenciado en commits del flujo crítico; CI verde (build+test+SAST/gitleaks+Newman) | **0 diferenciadores a medias**: lo que se entrega, se entrega completo y documentado |
| **G6** | Seguridad proporcional | 8 prácticas mapeadas a OWASP Top 10 (2021) implementadas y documentadas; SAST/gitleaks en CI sin hallazgos críticos; 401/403 correctos; **cero secretos en el repositorio** | Seguridad al alcance: se documenta *readiness* de PCI/pagos, no se implementa |
| **G7** | Lectura y escritura coexisten sobre la misma BD | Bajo la carga concurrente de escritura del test G1, el **p95/p99 de la búsqueda se mantienen estables** (sin degradación observable) y ninguna búsqueda se bloquea esperando una escritura. El *cómo* (proyección + Redis, bloqueo acotado a slots) está en NFR-1 | Sin optimización prematura: SQL Server en instancia única basta a la escala objetivo (~10k reservas/día) |

## 3. Actores y mini-journeys

Dos actores, dos recorridos. Los journeys aportan el *por qué* de cada capacidad; el detalle de datos y reglas vive en §4 (Features y FRs).

### Actor 1 — Agente (protagonista: **Carolina**)

Carolina incorpora un hotel nuevo al catálogo: registra nombre, ciudad, dirección y descripción. Le añade habitaciones (tipo, costo base, impuestos, ubicación) y las habilita. Semanas después corrige el precio de una habitación sin tocar el resto del hotel, y deshabilita temporalmente una habitación en mantenimiento, que deja de ofertarse al instante. Al cierre de mes entra a **ver las reservas de sus hoteles** y abre el detalle de cada una para conciliar comisiones. También **resuelve las solicitudes de cancelación**: habla con el viajero para entender el motivo y decide con criterio — aprobar aplicando la penalidad sugerida, condonarla, o **rechazar** la solicitud (la reserva sigue en pie). Cuando aprueba, la habitación vuelve a quedar disponible; toda decisión queda registrada. Nunca ve inventario ni reservas de otros agentes.

### Actor 2 — Viajero (protagonista: **Andrés**)

Andrés busca alojamiento en **Cartagena, del 10 al 14 de agosto, para 2 huéspedes**. El sistema le devuelve solo habitaciones activas, con capacidad suficiente y libres en todo el rango. Elige una e inicia la reserva registrando los **datos de cada huésped** y un **contacto de emergencia**. Confirma: ve el precio total `(costoBase + impuesto) × noches`, la reserva queda confirmada y **le llega un correo** (igual que al agente del hotel). Si otro viajero intentó la misma habitación y fechas en el mismo instante, uno confirma y el otro recibe un rechazo claro (409). Si más adelante Andrés no puede viajar, **solicita la cancelación** indicando el motivo; el sistema le devuelve una **penalidad estimada** según la antelación al check-in (0 % si faltan ≥30 días, 100 % si falta menos) —marcada como estimación— y un agente la revisa y decide: aplicarla, condonarla o rechazarla (en cuyo caso su reserva sigue confirmada).

## 4. Features y requisitos funcionales

Features agrupadas por capacidad; los FR llevan ID global estable (`FR-N`). Cada FR mapea a una capacidad del SPEC (`CAP-N`) o se marca **[NUEVO]** cuando amplía el contrato (cancelación).

### F1 — Gestión de hoteles e inventario (Agente) · CAP-1, CAP-2

- **FR-1** — El agente crea un hotel con nombre, ciudad, dirección, descripción y estado (habilitado/deshabilitado).
- **FR-2** — El agente edita los datos de un hotel existente.
- **FR-3** — El agente elimina lógicamente un hotel (**soft delete**): se marca inactivo sin borrado físico; un hotel inactivo no aparece en búsquedas ni oferta habitaciones.
- **FR-4** — El agente habilita/deshabilita un hotel; el cambio se refleja de inmediato en la ofertabilidad.
- **FR-5** — El agente añade una habitación a un hotel con tipo, costo base, impuestos, ubicación y estado.
- **FR-6** — El agente edita una habitación de forma independiente del hotel (editar la habitación no altera el hotel).
- **FR-7** — El agente habilita/deshabilita una habitación individualmente; una habitación deshabilitada, o perteneciente a un hotel deshabilitado, no se oferta.

### F2 — Búsqueda y reserva (Viajero) · CAP-4, CAP-5

- **FR-8** — El viajero busca habitaciones disponibles por ciudad de destino, fecha de entrada, fecha de salida y cantidad de huéspedes. El resultado incluye solo habitaciones activas, con capacidad ≥ huéspedes y libres en todo el rango `[entrada, salida)`; las ya reservadas en ese rango no aparecen.
- **FR-9** — El viajero crea una reserva sobre una habitación devuelta como disponible; la reserva se **crea y confirma en una sola operación** (sin estado intermedio de borrador), quedando en estado `Confirmada` de forma atómica con la reserva de los slots de inventario (ver FR-18).
- **FR-10** — La reserva registra los datos completos de **cada huésped**: nombres, apellidos, fecha de nacimiento, género, tipo y número de documento, email y teléfono. Todos los campos son **obligatorios** y se validan (formato de email, teléfono y documento; fecha de nacimiento coherente) con FluentValidation; una petición con campos faltantes o inválidos recibe **400** (Problem Details).
- **FR-11** — La reserva registra un **contacto de emergencia** (nombre completo y teléfono).
- **FR-12** — El sistema calcula el precio total `= (costoBase + impuesto) × noches` y lo expone al confirmar.

### F3 — Reservas del agente y ciclo de vida (cancelación) · CAP-3, CAP-10, CAP-11

- **FR-13** — El agente lista las reservas realizadas en **sus** hoteles y consulta el detalle completo de cada una; no ve reservas de otros agentes.
- **FR-14** — **Solicitud de cancelación.** El viajero solicita la cancelación de su propia reserva `Confirmada` con estancia no iniciada; el **agente puede iniciarla en su nombre** cuando el viajero contacta a la agencia. Se registra el **motivo** (categoría + texto libre) y su origen (viajero/agente), la reserva pasa a `CancelacionSolicitada` y se **congela la penalidad sugerida**. Una segunda solicitud sobre una reserva con solicitud en curso recibe **409**.
- **FR-15** — **Política sugerida (default).** Con la fecha de la solicitud como referencia: **≥30 días** al check-in → **0 %**; **<30 días** → **100 %** del valor. Es una **sugerencia calculada**, no una imposición; sin cobro real (monto adeudado).
- **FR-16** — **Resolución por el agente (discreción, auditada).** El agente del hotel resuelve: **aprobar aplicando** la penalidad sugerida, **aprobar condonándola**, o **rechazar**. Aprobar → `Cancelada`, **libera los slots** (la habitación vuelve a ofertarse), registra la **penalidad decidida** (con flag default/override) y quién decidió. Rechazar → vuelve a `Confirmada` con **motivo de rechazo**, sin liberar slots. Una segunda resolución concurrente → **409** (guard de estado + `rowversion`); un agente ajeno al hotel → **403**.
- **FR-17** — **Atajo, ciclo de vida y visibilidad.** El agente puede **solicitar y resolver en una sola operación** (viajero atendido por teléfono), registrando ambos eventos para auditoría. El ciclo `Confirmada → CancelacionSolicitada → {Cancelada | Confirmada}` se aplica con **guards**; las solicitudes pendientes exponen su **antigüedad ("días en espera")**, sin expiración automática.

### F4 — Garantía anti-overbooking · CAP-6

- **FR-18** — Nunca coexisten dos reservas activas para la misma habitación con estancias solapadas, **aun bajo concurrencia**. La garantía reside en el **motor de datos** (índice `UNIQUE (HabitacionId, Noche)` sobre los slots de inventario, que arbitra el conflicto en el propio INSERT bajo **READ COMMITTED**; `SERIALIZABLE` se descartó por costo/contención — ADR-016), nunca en lógica de aplicación; el intento perdedor recibe **409 Conflict** (Problem Details).

### F5 — Notificaciones · CAP-7 (+ notificaciones de CAP-10/CAP-11)

- **FR-19** — Al confirmarse una reserva, el sistema notifica por correo al huésped y al agente, **sin pérdida** (Transactional Outbox) ni **duplicado** (idempotencia por message-id).
- **FR-20** — Al **solicitarse** una cancelación, el sistema avisa al agente (por resolver) y envía al viajero un **acuse con la penalidad estimada**, marcada explícitamente como estimación (no el cobro final).
- **FR-21** — Al **resolverse**, el sistema notifica al viajero (y al agente): aprobación con **penalidad final** (con nota del agente si difiere de la estimada), **condonación**, o **rechazo indicando que la reserva sigue `Confirmada`** y su motivo.

### F6 — Seguridad y acceso · CAP-8

- **FR-22** — Toda operación exige autenticación (JWT/OIDC); una petición sin token válido recibe **401**.
- **FR-23** — La autorización es por rol (**Agente / Viajero**) y se resuelve server-side; un rol sin permiso recibe **403**.
- **FR-24** — Aislamiento entre agentes: un agente no puede leer ni modificar hoteles o reservas de otro agente.

### F7 — Observabilidad · CAP-9

- **FR-25** — Traza distribuida con `trace-id` propagado Gateway→servicio→sidecar Dapr→worker; ante un fallo, el span exacto del servicio/operación es visible.
- **FR-26** — Métricas de duración **p95/p99** por endpoint para detectar degradación.

## 5. Requisitos no funcionales (transversales)

El detalle técnico de cómo se cumplen estas NFR ya es contrato en los companions del SPEC (`docs/specs/spec-hotel-booking-hub/`); aquí se enuncian como requisito y se referencia el detalle, sin duplicarlo.

- **NFR-1 · Rendimiento y escalabilidad** — La búsqueda de disponibilidad se sirve por proyección de lectura + caché Redis; su p95/p99 se mantienen estables bajo carga concurrente de escritura (G7). Escala objetivo ≈10.000 reservas/día; SQL Server en instancia única para escritura; microservicios escalables de forma independiente. Ver [stack-and-conventions](../../../specs/spec-hotel-booking-hub/stack-and-conventions.md) y [concurrency-and-messaging](../../../specs/spec-hotel-booking-hub/concurrency-and-messaging.md).
- **NFR-2 · Concurrencia y consistencia** — Invariante anti-overbooking garantizado en el motor de datos (índice `UNIQUE (HabitacionId, Noche)` sobre los slots, arbitrado en el INSERT bajo **READ COMMITTED** — `SERIALIZABLE` descartado, ADR-016); concurrencia optimista con `rowversion` en los agregados; consistencia eventual entre Bounded Contexts vía eventos + proyecciones. Ver [concurrency-and-messaging](../../../specs/spec-hotel-booking-hub/concurrency-and-messaging.md).
- **NFR-3 · Fiabilidad de mensajería** — Entrega *at-least-once* (Transactional Outbox en la misma transacción que la reserva) + procesamiento *effectively-once* (idempotencia por message-id en Redis) + DLQ/retries; el sistema sobrevive a una caída del broker sin perder eventos. Ver [concurrency-and-messaging](../../../specs/spec-hotel-booking-hub/concurrency-and-messaging.md).
- **NFR-4 · Seguridad** — JWT/OIDC + RBAC server-side; 8 prácticas mapeadas a OWASP Top 10 (2021); **cero secretos en el repositorio** (Dapr Secrets en local / Key Vault en nube); SAST + gitleaks en CI. Ver [security-and-quality](../../../specs/spec-hotel-booking-hub/security-and-quality.md).
- **NFR-5 · Observabilidad** — OpenTelemetry (trazas, métricas, logs) con `trace-id` propagado de extremo a extremo; dashboard de Aspire en local / Application Insights en nube. Ver [security-and-quality](../../../specs/spec-hotel-booking-hub/security-and-quality.md) y [architecture-diagrams](../../../specs/spec-hotel-booking-hub/architecture-diagrams.md).
- **NFR-6 · Portabilidad y despliegue** — `docker compose up` levanta el sistema completo **sin instalar SDK ni Aspire**; *cloud-agnostic* vía Dapr (RabbitMQ local / Service Bus nube, seleccionado por componente); despliegue a Azure **exclusivamente por Terraform (IaC)**, sin click-ops. Ver [decisions-adr](../../../specs/spec-hotel-booking-hub/decisions-adr.md) ADR-002/007/008.
- **NFR-7 · Mantenibilidad y calidad** — Clean Architecture + DDD + CQRS (mediator propio); Result Pattern; Problem Details RFC 7807; `DateTimeOffset` (nunca `DateTime`); UUID v7 en PKs; versionado por URL `/api/v1/`. Cobertura ≥80% en código nuevo; TDD (Red→Green→Refactor) en el flujo crítico (cálculo de precio + creación de reserva). Ver [stack-and-conventions](../../../specs/spec-hotel-booking-hub/stack-and-conventions.md), [patterns](../../../specs/spec-hotel-booking-hub/patterns.md) y [delivery-and-testing](../../../specs/spec-hotel-booking-hub/delivery-and-testing.md).
- **NFR-8 · Contratos de API** — Cada microservicio expone REST documentado con OpenAPI; UI vía Scalar; contratos versionados. Ver [stack-and-conventions](../../../specs/spec-hotel-booking-hub/stack-and-conventions.md) y [decisions-adr](../../../specs/spec-hotel-booking-hub/decisions-adr.md) ADR-011.

## 6. Alcance y plan por fases

**Regla de oro:** no se pasa de fase sin cerrar la anterior. Azure es lo primero que se recorta si el tiempo aprieta (queda como IaC documentada).

| Fase | Objetivo | Incluye (FRs) |
|------|----------|---------------|
| **0 · Discovery (BMAD)** | Documentación y esqueleto | Contrato SPEC + este PRD → `architecture.md`, `docs/adr/`; esqueleto .NET 10 + Aspire |
| **1 · Core blindado** | Funcionalidad base impecable | **F1** (FR-1…7), **F2** (FR-8…12), **F3 núcleo de cancelación** (FR-13…17), **F4** anti-overbooking (FR-18); **F6** seguridad de acceso (FR-22…24); TDD del flujo crítico; colección Postman |
| **2 · Diferenciadores** | Nivel senior/lead | **F5** notificaciones (FR-19…21 vía eventos Dapr + Outbox/idempotencia); resto de las 8 prácticas de seguridad + OWASP; **F7** observabilidad OTel (FR-25, 26) |
| **3 · Nube (con compuerta)** | Despliegue real | Terraform → ACA + Azure SQL + Cache for Redis + Service Bus + Key Vault + App Insights |

> **Nota de secuenciación (resuelta).** El **núcleo de dominio** de la cancelación (solicitud, política/penalidad, transición de estado y liberación de slots — FR-14…17) va en **Fase 1**. Sus **notificaciones por correo** (FR-20, FR-21) dependen de la infraestructura de eventos, así que viajan con la notificación de reserva (FR-19) en **Fase 2**. Así la Fase 1 queda como core puro (HTTP + BD + dominio) y toda la mensajería asíncrona se concentra en la Fase 2 — el límite arquitectónico natural.

## 7. Supuestos, no-objetivos y preguntas abiertas

### Supuestos

- El evaluador ejecuta el sistema vía `docker-compose` sin instalar el SDK de .NET ni los workloads de Aspire.
- `.NET 10` es aceptable como el "`.NET 8 o superior`" que pide la vacante.
- La escala objetivo es ≈10.000 reservas/día (~0,12 writes/s promedio), lo que justifica SQL Server en instancia única para escritura.
- **[ASSUMPTION]** La penalidad **sugerida** se congela en la fecha de la solicitud del viajero (no en la de resolución del agente), para no perjudicarlo por la demora administrativa; la penalidad **decidida** por el agente puede diferir del default.
- **[ASSUMPTION]** Sin pagos reales (PCI fuera de alcance): la penalidad se **registra como monto adeudado**, no se cobra ni se reembolsa.

### No-objetivos

- **Multi-tenant / SaaS multi-agencia** — fuera de alcance; el producto sirve a una única agencia operadora.
- **Modificación / reprogramación de una reserva existente** — fuera de alcance; se resuelve **cancelando y creando una nueva reserva**.
- **Aviso automático al "segundo interesado" / waitlist** al liberarse inventario por cancelación — diseñado como gancho (evento `ReservaCancelada` enriquecido), **no implementado** (futuro).
- **Expiración automática (SLA) de solicitudes de cancelación pendientes** — fuera de alcance; se expone su antigüedad ("días en espera"), sin auto-resolución.
- **Read model en MongoDB** — diseñado y documentado como evolución CQRS, no implementado (Redis cubre el caché de lectura). Ver [decisions-adr](../../../specs/spec-hotel-booking-hub/decisions-adr.md) ADR-013.
- **Pagos / PCI DSS** — fuera de alcance; solo se documenta *readiness*.
- **Entra ID / IdP externo en nube** — descartado; emisor JWT/OIDC propio en local y en nube.
- **Front-end / interfaz de usuario** — la entrega es exclusivamente back end.
- **Máquina de estados formal y event sourcing** — bastan enum + guards en el aggregate.
- **Invocación síncrona servicio-a-servicio entre dominios** — se prefieren eventos.
- **Despliegue manual / click-ops en Azure** — el aprovisionamiento es exclusivamente por Terraform.

### Preguntas abiertas

Ninguna pendiente. La única que surgió —**PA-1**, sobre la secuenciación de las notificaciones de cancelación— se resolvió en §6.

## 8. Trazabilidad y referencias

- **Contrato canónico:** [SPEC-hotel-booking-hub](../../../specs/spec-hotel-booking-hub/SPEC.md) + companions.
- **Requisito → capacidad → solución:** ver [delivery-and-testing](../../../specs/spec-hotel-booking-hub/delivery-and-testing.md) (matriz de trazabilidad); cada encabezado de feature en §4 indica su(s) CAP. La cancelación (FR-14…17, FR-20, FR-21) quedó reflejada en el SPEC como **CAP-10** (solicitud + política) y **CAP-11** (tramitación), con **ADR-014**.




