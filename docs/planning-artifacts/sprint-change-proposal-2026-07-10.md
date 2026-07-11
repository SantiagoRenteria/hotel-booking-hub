# Sprint Change Proposal — Transporte real de eventos (cierre de brecha Dapr/RabbitMQ)

**Fecha:** 2026-07-10 · **Autor:** correct-course (dev, modo autónomo) · **Decisión:** party-mode (John+Winston+Amelia) + Santiago · **Modo:** Batch.

## 1. Resumen del problema

Al cerrar la Épica 7 se detectó que **el transporte de eventos entre Bounded Contexts nunca se cableó**. El diseño hexagonal de mensajería está entregado y probado en las costuras (Outbox transaccional, idempotencia Redis SETNX+TTL, contratos versionados con contract tests, consumidores, dead-letter), pero:

- El único adaptador de `IPublicadorEventos` es `PublicadorEventosLog` (solo escribe al log).
- `Notificaciones.Worker` solo late; sus consumidores se invocan **solo en tests**.
- No hay sidecars Dapr en `docker-compose`, ni paquete/código Dapr; `deploy/dapr/*.yaml` son placeholders.

**Consecuencia:** `docker compose up` levanta todo, pero **crear una reserva NO dispara una notificación real** por un broker + worker. El diferenciador senior/lead (eventos) está "documentado + probado en costuras", no "corriendo end-to-end". Se difirió épica tras épica (registrado en `deferred-work.md`) y nunca se cerró.

## 2. Análisis de impacto

- **Épicas:** E1/E3/E5 entregaron el diseño; el transporte quedó como deuda transversal. Se crea **Épica 9** para cerrarlo sin reabrir el `done` de E5 (que entregó la lógica de notificación correctamente).
- **Historias:** nueva **Story 9.1** (transporte real por RabbitMQ local). No modifica historias existentes; reutiliza `DespachadorNotificaciones`, inbox idempotente y dead-letter tal cual.
- **Arquitectura:** ADR-002 ("Dapr para pub/sub + secretos") se **refina** con dos ADRs nuevos que explicitan la realización por entorno: **ADR-019** (transporte Strategy: RabbitMQ local / Dapr→Service Bus nube) y **ADR-020** (secretos: env vars local / Dapr Secrets+Key Vault nube). AC-E8.1.2 ("cambiar de broker = solo YAML") se matiza: aplica **dentro** del adaptador Dapr en nube; el salto local→nube selecciona el adaptador por entorno (el dominio no cambia).
- **Técnico:** nuevos adaptadores `PublicadorEventosRabbitMq` (×2 BC) + consumidor `BackgroundService` en el worker + selección DI por entorno + test de integración Testcontainers RabbitMQ + healthcheck de readiness en compose. RabbitMQ ya está en compose y en el AppHost.
- **Secretos:** sin cambio de código; ADR-020 registra la decisión ya vigente (env vars). Outcome "cero secretos en repo" ya se cumple.

## 3. Enfoque recomendado

**Direct Adjustment** — añadir Épica 9 + Story 9.1 al plan y ejecutarla por el ciclo autónomo de 5 pasos. Es el cuadrante barato-de-cerrar/caro-de-que-te-lo-encuentren; va **antes** de E8 (recortable) y de cerrar ET (que no puede documentar honestamente un flujo que no corre). Esfuerzo: **M** (Amelia). Riesgo: bajo (reusa el puerto y el despacho existentes; test-first con Testcontainers dentro de `dotnet test`). Timebox recomendado (John): si no corre end-to-end limpio en el presupuesto, documentar limitación y sacrificar E8 — no hundirse en infraestructura.

## 4. Cambios propuestos (aplicados en este proposal)

1. **epics.md** — nueva sección **## Epic 9** + **### Story 9.1** (con AC-E9.1.1…5) + entrada en el resumen de épicas.
2. **sprint-status.yaml** — `epic-9`, `9-1-transporte-real-de-eventos-rabbitmq`, `epic-9-retrospective`.
3. **decisions-adr.md** — **ADR-019** (transporte Strategy por entorno) + **ADR-020** (secretos por entorno).
4. **deferred-work.md** — el adaptador Dapr de nube + Dapr Secrets/Key Vault se re-encuadran como trabajo de la Épica 8 (no "diferido indefinido").

## 5. Handoff

**Scope: Moderate** (reorganización de backlog: nueva épica + historia + ADRs). Ejecutor: **Developer (Amelia)** por el ciclo autónomo — `create-story 9-1` → `dev-story` (TDD, Testcontainers RabbitMQ) → `code-review` (3 capas) → `agent-dev` (fixes) → rama feature → PR a `develop` → CI verde → merge. Criterio de éxito: `docker compose up` + crear una reserva dispara la notificación por RabbitMQ, verificado por un test de integración en verde dentro de `dotnet test`.
