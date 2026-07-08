---
id: SPEC-hotel-booking-hub
companions:
  - glossary.md
  - architecture-diagrams.md
  - concurrency-and-messaging.md
  - stack-and-conventions.md
  - security-and-quality.md
  - patterns.md
  - decisions-adr.md
  - delivery-and-testing.md
sources:
  - ../../DOCUMENTO-BASE.md
---

> **Contrato canónico.** Este SPEC y los archivos en `companions:` son el contrato completo y validado por preservación de qué construir, probar y validar. El documento fuente en el frontmatter es solo para trazabilidad — consúltalo únicamente si necesitas el razonamiento narrativo o el color de prosa que este contrato omite a propósito.

# Sistema de Gestión y Reserva de Hoteles (hotel-booking-hub)

## Why

Es una **prueba técnica de Back End Developer para UltraGroup** (mandato/evaluación) que resuelve un **dolor de negocio** real: una agencia de viajes gestiona hoteles y reservas de forma manual, lo que genera inconsistencias, pérdida de comisiones y mala experiencia para el viajero. El objetivo es el back end de una plataforma de alojamiento robusta, escalable y mantenible. El criterio de evaluación es explícito y ancla todos los trade-offs: *"no se espera perfección en todos los frentes: se valorará la claridad del razonamiento detrás de las decisiones tomadas"* → gana **core impecable + pocos diferenciadores con profundidad real + documentación que justifica cada decisión (incluida la de no hacer algo)**.

## Capabilities

- id: CAP-1
  intent: El agente puede crear, editar y eliminar lógicamente hoteles con nombre, ciudad, dirección, descripción y estado (habilitado/deshabilitado).
  success: Se crea un hotel y se recupera; una edición cambia sus valores; una eliminación lo marca inactivo (soft delete) sin borrarlo físicamente; un hotel deshabilitado no aparece en búsquedas.

- id: CAP-2
  intent: El agente puede asignar y gestionar habitaciones de un hotel (tipo, costo base, impuestos, ubicación, estado), editándolas de forma independiente del hotel.
  success: Se añade una habitación con esos campos, se edita sin afectar al hotel y se habilita/deshabilita individualmente; una habitación deshabilitada o de un hotel deshabilitado no se oferta.

- id: CAP-3
  intent: El agente puede listar las reservas realizadas en sus hoteles y ver el detalle de cada una.
  success: El listado devuelve solo reservas de los hoteles del agente autenticado y expone el detalle completo de cada reserva.

- id: CAP-4
  intent: El viajero puede buscar habitaciones disponibles por ciudad de destino, fecha de entrada, fecha de salida y cantidad de huéspedes.
  success: La búsqueda devuelve solo habitaciones activas en la ciudad, con capacidad ≥ huéspedes y libres en todo el rango [entrada, salida); las ya reservadas en ese rango no aparecen.

- id: CAP-5
  intent: El viajero puede reservar una habitación disponible registrando los datos completos de cada huésped y un contacto de emergencia.
  success: Una reserva confirmada persiste por huésped (nombres/apellidos, fecha de nacimiento, género, tipo y número de documento, email, teléfono) y un contacto de emergencia (nombre completo, teléfono); el precio es (costoBase + impuesto) × noches.

- id: CAP-6
  intent: El sistema garantiza cero overbooking — nunca coexisten dos reservas activas para la misma habitación con estancias solapadas, aun bajo concurrencia.
  success: Dos solicitudes concurrentes para la misma habitación y fechas solapadas producen exactamente una reserva confirmada; la otra recibe 409 Conflict (Problem Details), garantizado por el motor de datos y verificado con test de integración sobre SQL Server real. Ver [[concurrency-and-messaging]].

- id: CAP-7
  intent: El sistema notifica por correo al huésped y al agente al confirmar una reserva, sin perder ni duplicar la notificación.
  success: Tras confirmar, huésped y agente reciben correo; el evento sobrevive a caída del broker (permanece en el outbox y se publica al reponerse) y un evento repetido no genera correo duplicado (idempotencia por message-id). Ver [[concurrency-and-messaging]].

- id: CAP-8
  intent: El acceso a las operaciones está autenticado y restringido por rol (Agente/Viajero) con autorización server-side.
  success: Una petición sin token válido recibe 401; un rol sin permiso recibe 403; un agente no puede leer ni modificar hoteles/reservas de otro agente. Ver [[security-and-quality]].

- id: CAP-9
  intent: Un operador puede rastrear de extremo a extremo dónde falló o se degradó una petición a través de los servicios.
  success: Ante un fallo, la traza distribuida (trace-id propagado Gateway→servicio→sidecar Dapr→worker) muestra el span del servicio/operación exacto; existen métricas de duración p95/p99 por endpoint para detectar degradación.

- id: CAP-10
  intent: El viajero (o el agente en su nombre, si el viajero contacta a la agencia) puede solicitar la cancelación de una reserva confirmada antes de que inicie la estancia, indicando el motivo; el sistema calcula y congela una penalidad **sugerida** por política.
  success: La solicitud sobre una reserva `Confirmada` con estancia futura la pasa a `CancelacionSolicitada`, registra el motivo (categoría + texto libre) y su origen (viajero/agente), y **congela** la penalidad sugerida (referencia = fecha de solicitud): 0% si faltan ≥30 días para el check-in, 100% del valor si faltan <30 días. Una solicitud sobre una estancia ya iniciada o pasada, sobre una reserva que ya tiene una solicitud en curso, o de un viajero ajeno a la reserva, se rechaza. Ver [[glossary]].

- id: CAP-11
  intent: El agente del hotel resuelve la solicitud con discreción — tras entender el motivo puede aprobar aplicando la penalidad sugerida, aprobar condonándola, o rechazar la solicitud.
  success: Al **aprobar**, la reserva pasa a `Cancelada`, se **liberan los slots** de inventario (la habitación vuelve a estar disponible en esas fechas) y se registra la penalidad **decidida** (igual o distinta del default, con flag default/override) y quién decidió; al **rechazar**, la reserva vuelve a `Confirmada` con el motivo del rechazo y sin liberar slots. Toda decisión queda auditada. Un agente ajeno al hotel no puede resolverla (403); una segunda resolución concurrente recibe 409. La penalidad se registra como monto adeudado (sin cobro real). Ver [[concurrency-and-messaging]].

## Constraints

- **Stack fijo:** C# / .NET 10 (Minimal API, sin MVC), SQL Server (una BD por microservicio), EF Core 10, Redis, REST documentado con OpenAPI. Detalle en [[stack-and-conventions]].
- **Repositorio público sin dependencias privadas**; debe compilar en la máquina del evaluador (excluye MediatR comercial y los paquetes privados de la base BMAD). Ver [[decisions-adr]] ADR-009.
- **El invariante anti-overbooking se garantiza en el motor de datos** (unicidad de slots de inventario + transacción `SERIALIZABLE`), nunca en lógica de aplicación. Ver [[concurrency-and-messaging]].
- **Entrega de eventos at-least-once** (Transactional Outbox en la misma transacción que la reserva) **+ procesamiento effectively-once** (idempotencia por message-id en Redis + DLQ/retries).
- **Arquitectura de ≥2 Bounded Contexts** como microservicios independientes con contratos OpenAPI, comunicados por eventos (sin acoplamiento síncrono entre dominios). Ver [[glossary]] y [[architecture-diagrams]].
- **TDD obligatorio** (Red→Green→Refactor con commits que lo evidencien) para el flujo crítico (cálculo de precio + creación de reserva); cobertura ≥80% en código nuevo.
- **Seguridad:** JWT/OIDC + RBAC server-side + prácticas mapeadas a OWASP Top 10 (2021). Detalle en [[security-and-quality]].
- **Código de dominio en español sin tildes** en identificadores; sufijos de patrón/capa en inglés; comentarios y textos de negocio en español con tildes. Detalle en [[stack-and-conventions]].
- **Reglas técnicas obligatorias:** `DateTimeOffset` (nunca `DateTime`), UUID v7 en todas las PKs, Result Pattern para flujos esperados, Problem Details RFC 7807 en errores, concurrencia optimista con `rowversion`, versionado por URL `/api/v1/`. Detalle en [[stack-and-conventions]].
- **Entregables obligatorios:** README con diagramas C4 + ADRs, `docker-compose` funcional, colección Postman/Newman, documentación de prácticas de seguridad y de uso de IA. Detalle en [[delivery-and-testing]].
- **`docker-compose` se mantiene a mano** (no se genera desde `Aspire.Hosting.Docker`), blindado con un smoke test en CI (`docker compose up` + verificación de `/health`) que detecta drift. Ver [[decisions-adr]] ADR-007.
- **Despliegue a Azure exclusivamente por IaC (Terraform)** — nunca provisión manual ni click-ops; el entregable de nube es la IaC ejecutable, bajo compuerta de Fase 3. Ver [[decisions-adr]] ADR-008.
- **Autenticación con JWT/OIDC propio en local y en nube** — no se adopta Entra ID. Ver [[decisions-adr]] ADR-006.
- **Autoría de commits:** todos con autor Santiago Renteria; sin trailers de coautoría ni firmas de herramientas de IA.
- **Ciclo de vida de la reserva por enum + guards** en el aggregate (`Confirmada → CancelacionSolicitada → {Cancelada | Confirmada}`), sin máquina de estados formal ni event sourcing. La **política de cancelación** (≥30 días → 0%, <30 días → 100%, referencia = fecha de solicitud) es una **sugerencia calculada** que el agente puede anular con discreción (aplicar, condonar o rechazar); toda decisión queda auditada. Ver [[decisions-adr]] ADR-014.
- **Regla de oro de ejecución:** no se avanza de fase sin cerrar la anterior; la nube (Azure) es lo primero que se recorta si el tiempo aprieta. Ver [[delivery-and-testing]].

## Non-goals

- **Read model en MongoDB** — diseñado y documentado como evolución CQRS, **no implementado** en esta entrega (Redis cubre el caché de lectura). Ver [[decisions-adr]] ADR-013.
- **Pagos / PCI DSS** — fuera de alcance; solo se documenta *readiness*, no se implementa.
- **Entra ID / IdP externo en nube** — descartado; se usa el emisor JWT/OIDC propio también en la nube.
- **Front-end / interfaz de usuario** — la entrega es exclusivamente back end.
- **Máquina de estados formal del ciclo de la reserva y event sourcing** — fuera de alcance (basta enum + guards en el aggregate).
- **Invocación síncrona servicio-a-servicio entre dominios** — se prefieren eventos; sin acoplamiento síncrono entre BCs.
- **Despliegue manual / click-ops en Azure** — fuera de alcance; el aprovisionamiento es exclusivamente por Terraform (bajo compuerta de Fase 3).
- **Multi-tenant / SaaS multi-agencia** — fuera de alcance; el producto sirve a una única agencia operadora (single-tenant).
- **Modificación / reprogramación de una reserva existente** — fuera de alcance; se resuelve cancelando y creando una nueva reserva.
- **Aviso automático al "segundo interesado" / waitlist** al liberarse inventario por cancelación — diseñado como gancho (el evento `ReservaCancelada` se enriquece con hotel, tipo de habitación y fechas liberadas), **no implementado** (evolución futura).
- **Expiración / auto-resolución (SLA) de solicitudes de cancelación pendientes** — fuera de alcance; la solicitud espera la decisión del agente y solo se expone su antigüedad ("días en espera").

## Success signal

El evaluador hace `docker compose up` sin instalar SDK ni Aspire, autentica como agente y como viajero, crea un hotel con habitaciones y lanza **dos reservas concurrentes sobre la misma habitación y fechas**: exactamente una queda confirmada (con correos entregados a huésped y agente) y la otra se rechaza con 409 — y el recorrido completo es visible en el dashboard de observabilidad. El razonamiento de cada decisión (y de cada no-decisión) queda justificado en README y ADRs.

## Assumptions

- El evaluador ejecuta el sistema vía `docker-compose` sin instalar el SDK de .NET ni los workloads de Aspire (objetivo de reproducibilidad declarado en la base).
- `.NET 10` es aceptable como "`.NET 8 o superior`" que pide la vacante.
- La escala objetivo es del orden de 10.000 reservas/día (≈0,12 writes/s promedio), lo que justifica SQL Server en instancia única para escritura.
- La penalidad **sugerida** se congela en la fecha de la solicitud del viajero (no en la de resolución del agente), para no perjudicar al viajero por la demora administrativa; la penalidad **decidida** por el agente puede diferir del default.
- Sin pagos reales (PCI fuera de alcance): la penalidad (sugerida o decidida) se registra como monto adeudado, no se cobra ni se reembolsa.
