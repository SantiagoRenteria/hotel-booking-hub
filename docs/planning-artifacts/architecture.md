---
stepsCompleted: [1, 2]
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
