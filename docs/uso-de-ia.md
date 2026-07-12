# Uso de IA en la construcción de hotel-booking-hub

> Navegación: [README](../README.md) · [ADRs](adr/README.md) · [Épicas](planning-artifacts/epics.md) · [Historias](implementation-artifacts/) · [BDD y E2E](bdd-y-e2e.md) · **Uso de IA** (este documento)

Este proyecto se construyó con asistencia de IA (Claude, modelo `claude-opus-4-8`) operada bajo el **método BMAD**: un conjunto de *skills* y *agentes* (personas) que estructuran el trabajo por historias, con TDD visible, revisión adversarial y decisiones arquitectónicas deliberadas. Este documento explica cómo se usó la IA de forma honesta y concreta, incluyendo sus límites.

La autoría del código es de **Santiago Rentería**. La IA asistió bajo su dirección: las decisiones clave (alcance, arquitectura, cruzar la compuerta de despliegue real, gasto en la nube) las aprobó él explícitamente antes de ejecutarse.

---

## 1. El flujo BMAD de punta a punta (planificación → construcción → entrega)

BMAD no es solo "escribir historias con TDD": es un método que cubre **desde la idea hasta la entrega**, y en este proyecto se recorrió en ese orden. Cada fase tiene su skill y deja un **artefacto real** en el repo que alimenta a la siguiente (contexto acumulativo, no conversaciones sueltas):

1. **Análisis y descubrimiento (Mary, analista · John, PM).** El dominio (hotelería: hoteles, inventario por habitación, reservas con anti-overbooking, cancelación con discreción del agente, notificaciones) se analizó para fijar el alcance y los actores (Agente, Viajero). Esta fase alimentó al PRD; se apoyó en las skills de analista/PM (`agent-analyst`, `agent-pm`).

2. **PRD — requisitos de producto (John · `prd`).** Se destiló en un PRD con requisitos funcionales (FR-1…FR-26) y no funcionales (seguridad, observabilidad, resiliencia): [`docs/planning-artifacts/prds/prd-hotel-booking-hub-2026-07-08/prd.md`](planning-artifacts/prds/prd-hotel-booking-hub-2026-07-08/prd.md). Es la fuente de la trazabilidad `FR-x → AC → test`.

3. **SPEC — contrato máquina (`spec`).** El PRD se condensó en un kernel SPEC validado (invariantes, contratos, criterios de preservación) que sirve de referencia canónica para lo aguas abajo: [`docs/specs/spec-hotel-booking-hub/SPEC.md`](specs/spec-hotel-booking-hub/SPEC.md) y sus compañeros (incluido `decisions-adr.md`, origen de los ADR).

4. **Arquitectura (Winston · `create-architecture`).** El diseño de solución —Clean Architecture + DDD + CQRS, mediador propio, Outbox+idempotencia, microservicios por Bounded Context, gateway YARP, JWT/OIDC+RBAC, OpenTelemetry— quedó en [`docs/planning-artifacts/architecture.md`](planning-artifacts/architecture.md), con las decisiones puntuales extraídas como **ADRs** (`docs/adr/`, ver §4).

5. **Épicas e historias (`create-epics-and-stories`).** El alcance se descompuso en épicas verticales y sus historias con criterios de aceptación en BDD y *source hints*: [`docs/planning-artifacts/epics.md`](planning-artifacts/epics.md).

6. **Sprint planning y seguimiento (`sprint-planning` / `sprint-status`).** El estado de cada historia/épica se rastrea en [`docs/implementation-artifacts/sprint-status.yaml`](implementation-artifacts/sprint-status.yaml) (backlog → ready-for-dev → in-progress → review → done), que es también lo que las skills leen para saber qué sigue.

7. **UX (Sally · `agent-ux-designer`).** Al ser una API headless (sin frontend en el alcance), la UX se limitó a los contratos de interacción (formas de request/response, códigos de estado, Problem Details); no hubo wireframes.

8. **Gestión del cambio (`correct-course`).** Cuando el alcance cambió a mitad de camino, no se improvisó: se produjeron **propuestas de cambio de sprint** formales — p. ej. [reabrir la Épica 8 para desplegar de verdad](planning-artifacts/sprint-change-proposal-2026-07-10-e8-despliegue-real.md) y [cerrar la brecha del transporte de eventos](planning-artifacts/sprint-change-proposal-2026-07-10.md).

Solo **después** de esta planificación se entró a construir historia por historia (§2). Ese orden —planear con artefactos versionados y luego ejecutar— es lo que hace el trabajo auditable de principio a fin.

---

## 2. El ciclo por historia (construcción)

El trabajo no se hizo "pidiéndole código a un chat". Cada historia recorrió un ciclo disciplinado, donde **cada paso se invoca por su propia skill** (nunca a mano), lo que garantiza reproducibilidad y evita atajos:

1. **`create-story` — contexto exhaustivo.** Antes de escribir una línea, se genera un archivo de historia con TODO lo que el desarrollador necesita: user story, criterios de aceptación en formato BDD (Given/When/Then), guardarraíles de arquitectura, archivos a tocar (leídos previamente), aprendizajes de la historia anterior y anti-patrones a evitar. El objetivo declarado de la skill es *prevenir los errores típicos de un LLM* (reinventar la rueda, librería equivocada, ruta equivocada, romper regresiones, mentir sobre el avance). Ejemplo real: `docs/implementation-artifacts/1-6c-money-test-confirmacion-unica-bajo-concurrencia.md`.

2. **`dev-story` — TDD Red→Green visible en commits.** La implementación sigue el ciclo red-green-refactor: primero un test que **falla** (commit `test:`), luego el código mínimo que lo pone en **verde** (commit `feat:`). En historias críticas ese ciclo se deja visible en el historial de commits, no colapsado, para exhibir la disciplina. La skill prohíbe marcar una tarea como completa si los tests no existen y pasan al 100% ("NO LYING OR CHEATING").

3. **`code-review` — 3 capas adversariales en paralelo.** El cambio se revisa con tres subagentes independientes lanzados en paralelo, cada uno con distinto nivel de contexto para maximizar la diversidad de hallazgos:
   - **Blind Hunter** — recibe *solo el diff*, sin spec ni contexto. Caza problemas intrínsecos del código.
   - **Edge Case Hunter** — recibe el diff más acceso de lectura al proyecto. Recorre ramas y condiciones de borde no manejadas.
   - **Acceptance Auditor** — recibe el diff, la spec y los documentos de contexto. Verifica que se cumplan los criterios de aceptación y la intención del diseño.

   Los hallazgos se triangulan en categorías accionables. En las historias se registran como **Review Findings** con triaje explícito: `[Patch]` (se arregla ya), `[Defer]` (se difiere con justificación) y `Dismiss` (se descarta con razón). Ejemplo real en `1-6c` (un `catch` de deadlock que solo atrapaba `DbUpdateException` y dejaba escapar un `SqlException` crudo como 500 → arreglado usando el mismo predicado `PoliticaReintentos.EsDeadlock`).

4. **Arreglar hallazgos (`agent-dev`).** Los hallazgos de la revisión se resuelven con el agente de desarrollo antes de avanzar. No se pasa a la siguiente historia sin cerrar los hallazgos de la actual.

5. **`party-mode` — cuando hay decisión arquitectónica.** Ante una bifurcación de diseño se convoca una mesa redonda donde cada persona es un **subagente real e independiente** (no un mismo LLM haciendo voces), de modo que discrepen de verdad. La decisión resultante la aprueba Santiago y se registra como ADR (ver §4 y `docs/adr/`).

6. **`correct-course` — cuando cambia el alcance.** Los cambios significativos a mitad de sprint se gestionan formalmente (p. ej. la decisión de desplegar de verdad a Azure fue un correct-course + party-mode).

7. **`retrospective` — al cerrar cada épica** (ver `docs/implementation-artifacts/epic-*-retro-*.md`).

**Entrega:** una PR por historia contra `develop`, con CI en verde (build, tests, `dotnet format`, gitleaks) antes del merge. El detalle de cómo se especifica el comportamiento (BDD) y cómo se verifica de extremo a extremo está en [BDD y E2E](bdd-y-e2e.md).

---

## 3. Agentes / personas y para qué se usaron

BMAD modela roles como personas con expertise propia. Los que participaron:

| Persona | Rol | Uso en el proyecto |
|---|---|---|
| **Amelia** | Desarrolladora senior (`agent-dev` / `dev-story`) | Implementación de historias con TDD; resolución de hallazgos de revisión. |
| **Winston** | Arquitecto (`agent-architect`) | Decisiones de diseño en party-mode: transporte de eventos, estrategia de state, scale-to-zero. |
| **John** | Product Manager (`agent-pm`) | Alcance, criterios de aceptación, control de costo (vetó el CD auto-apply que quemaría dinero). |
| **Mary** | Analista de negocio (`agent-analyst`) | Requisitos y contexto de dominio. |
| **Murat** | Test Architect (`tea`) | Estrategia de tests (p. ej. definición del "money test G1" de concurrencia). |
| **Sally** | Diseñadora UX (`agent-ux-designer`) | Patrones de interacción donde aplicaba. |
| **Paige** | Redactora técnica (`agent-tech-writer`) | Curaduría de documentación. |

En party-mode típicamente se convocaba a Winston + John + Amelia (y Mary/Murat según el tema), y Santiago cerraba la decisión.

---

## 4. Decisiones arquitectónicas como ADRs (prompts de módulos críticos)

Tres decisiones arquitectónicas salieron de party-mode + aprobación de Santiago y quedaron como ADRs:

- **Transporte de eventos (ADR-019 / ADR-020).** Al cerrar la Épica 7 se detectó que el pub/sub prometido por Dapr nunca se había cableado: el único adaptador de `IPublicadorEventos` era un placeholder (`PublicadorEventosLog`), así que los eventos no cruzaban ningún broker. *Prompt en esencia:* "¿cableamos el transporte real ahora, y con qué?". La mesa (John+Winston+Amelia) decidió una **Strategy por entorno detrás del puerto hexagonal**: en LOCAL/compose, **RabbitMQ directo** cableado de verdad y verificado con Testcontainers (Épica 9); en NUBE, **Dapr → Azure Service Bus**. Se descartó cablear Dapr también en local (esfuerzo alto en la fase de mayor riesgo). Los secretos quedaron por variables de entorno en local (ADR-020), cumpliendo "cero secretos en repo" por el mecanismo adecuado a cada entorno.

- **Cruzar la compuerta y desplegar de verdad (ADR-021 / ADR-022 / ADR-023).** *Prompt en esencia:* "vamos a desplegar de verdad a Azure para demostrar que la IaC funciona; ¿cómo lo hacemos sin quemar dinero ni introducir secretos?". Decisiones: CD por **OIDC federated** (cero secretos, sin Service Principal con contraseña); John **vetó** el auto-apply en cada merge por riesgo de gasto accidental, quedando el disparo **on-demand con aprobación humana**; **state remoto** por script de bootstrap idempotente con backend AAD y dos resource groups (state permanente / app efímero, ADR-022); y **scale-to-zero selectivo** — las 3 apps HTTP a `min=0`, pero el worker de Notificaciones a `min=1` para consumir eventos sin reintroducir un secreto de conexión de KEDA (ADR-023).

Todos los ADRs viven como archivos en `docs/adr/` y están reconciliados con el documento base.

---

## 5. Generación asistida de código: un caso concreto (crear-confirmar reserva)

Además de las decisiones, la IA generó y refactorizó **módulos críticos**. Aquí un caso end-to-end —el flujo que el enunciado nombra explícitamente, *"crear reserva"*— con el prompt real, la iteración y cómo se validó que el código quedara **seguro, limpio y alineado a la arquitectura**.

**Módulo:** write-path de creación de reserva — `CrearReservaCommandHandler` + `TransactionBehavior` + política de reintentos de deadlock + el invariante anti-overbooking (`UNIQUE(HabitacionId, Noche)`). Es el corazón transaccional del sistema: si falla bajo concurrencia, hay overbooking o errores 500.

**El "prompt" en BMAD = la historia como contrato + la invocación de `dev-story`.** No fue un chat suelto: la Story 1.6c fijó el criterio de aceptación en Gherkin como contrato máquina, y la instrucción al agente desarrollador (Amelia) fue, textualmente en esencia:

> *"Implementa la Story 1.6c siguiendo TDD estricto Red→Green **visible en commits**. Primero un test que **falle** ejerciendo **concurrencia real** (N solicitudes con `Task.WhenAll`) sobre el **pipeline de producción** con SQL Server real (Testcontainers), no un mock. El invariante: exactamente **1×201** y **N-1×409**, **cero excepciones no mapeadas** y **cero 500**. Luego el código mínimo para ponerlo en verde. No marques la tarea completa si el test no existe y pasa al 100%."*

**Cómo se iteró (Red → Green → refactor → revisión adversarial):**

1. **Red:** `MoneyTestG1Tests` lanza N `CrearReservaCommand` concurrentes; falla (aún no hay arbitraje del invariante).
2. **Green:** se arbitra el conflicto en el `INSERT` con el índice único de slots (READ COMMITTED, no SERIALIZABLE — ADR-016); la carrera perdedora viola el índice y se traduce a **409**.
3. **Revisión adversarial (el paso que atrapó el bug de seguridad/correctitud):** el *Blind Hunter* del `code-review` detectó que el `catch` de deadlock solo capturaba `DbUpdateException` y **dejaba escapar un `SqlException` crudo (error 1205, deadlock victim) como HTTP 500** en vez de 409. Se corrigió reusando el mismo predicado de dominio `PoliticaReintentos.EsDeadlock` en ambos puntos → el deadlock siempre degrada a 409, nunca a un 500 que filtraría un detalle de infraestructura.
4. **Verificación objetiva:** money test en verde y determinista — `1×201`, `N-1×409`, `1205` agotado → 409, cero 500. Corre aislado en CI (`Category=G1`, sin paralelizar).

**Por qué el resultado es "seguro, limpio y alineado":** el código generado no se aceptó por confianza en el modelo, sino porque (a) un test de concurrencia real sobre el pipeline de producción lo prueba, (b) una revisión adversarial independiente cazó un fallo que el camino feliz ocultaba, y (c) el fix reutiliza una abstracción de dominio existente (`PoliticaReintentos`) en vez de duplicar lógica. El mismo patrón (historia-contrato → TDD → revisión de 3 capas → fix) se aplicó a los otros módulos críticos (Outbox atómico, idempotencia de reserva, máquina de estados de cancelación). Ver [BDD y E2E](bdd-y-e2e.md) para el mapa completo historia↔test.

---

## 6. Iteración y verificación

Cada paso se validó con evidencia objetiva, no con la palabra del modelo:

- **Lógica de dominio:** tests verdes obligatorios. El "money test G1" (`1-6c`) ejerció **concurrencia real** (N solicitudes vía `Task.WhenAll` sobre el pipeline de producción con Testcontainers.MsSql) exigiendo exactamente `1×201` y `N-1×409`, cero excepciones no mapeadas, y `1205` agotado → 409 (nunca 500).
- **Infraestructura (IaC):** el "gate" no es TDD (aplicar red-green a Terraform sería teatro), sino `terraform fmt` + `validate` en verde, `plan` revisado, y finalmente `apply` reproducible + `smoke` + `destroy` limpio.
- **Extremo a extremo real:** smoke E2E contra Azure de verdad.

### La saga real de la Épica 8 (iteración honesta)

La Épica 8 dejó primero la IaC *validada pero no aplicada* (8.1). Al **desplegar de verdad** (8.2), la teoría chocó con la realidad de la suscripción, y cada obstáculo se diagnosticó y resolvió iterando:

1. **Azure SQL bloqueado en East US / East US 2** → se migró la región a **West US 2** (verificado por la capabilities API).
2. **Azure Cache for Redis clásico retirado** → se adoptó **Azure Managed Redis** (`Balanced_B0`).
3. **Cuenta invitada (#EXT#) sin permiso para asignar rol de datos** (`az role assignment create` → MissingSubscription) → backend del tfstate por **clave de cuenta** obtenida en runtime (ajuste de ADR-022; la clave nunca entra al repo).
4. **Consistencia eventual de ARM** (404 read-after-create) → **apply por fases (padres→dependientes) + reintentos**.
5. **Scale-to-zero poco fiable en servicios internos tras el gateway** → `hoteles`/`reservas` a `min=1`.
6. **Ruteo del gateway (YARP)** apuntaba a nombres de docker-compose → override por env a los nombres de app de ACA (`hbh-dev-hoteles`).

**Resultado:** 32 recursos provisionados y **flujo de negocio verificado end-to-end** contra Azure real — `GET /health` 200, crear hotel → habitación → reserva (precio `238.00` = (100+19)×2 noches) → cancelar, todo autenticado con JWT (clave desde Key Vault), ruteado por el gateway y persistido en Azure SQL. Evidencia en `docs/implementation-artifacts/evidencia/8-2-despliegue-real-smoke.md`. Luego `destroy` del RG-app efímero, sin recursos facturables residuales.

### Estado del transporte de eventos (actualizado)

El transporte evento → worker está cableado en **ambos entornos** tras el puerto `IPublicadorEventos` (Strategy, ADR-019):

- **LOCAL / compose:** fluye end-to-end por **RabbitMQ directo**, verificado con Testcontainers en la Épica 9 y de nuevo en el smoke del compose (crear reserva → evento → notificación del worker).
- **NUBE / ACA:** el adaptador **Dapr → Service Bus** (`PublicadorEventosDapr` + suscripción Dapr del worker) se **implementó** (cierre de brecha, seleccionado por `TransporteEventos=Dapr`). Queda **una verificación pendiente**: observar el evento → worker en runtime en el próximo despliegue a ACA (el sidecar Dapr solo existe en la nube). Es lo único abierto en `docs/implementation-artifacts/deferred-work.md`; **ya no** es un "no fluye en nube" como en la primera pasada de la Épica 8.

---

## 7. Transparencia

- **Autoría del código: Santiago Rentería.** La IA asistió bajo su dirección y aprobación.
- Las **decisiones clave las aprobó Santiago** explícitamente: alcance por épica, cada decisión arquitectónica de party-mode (que se volvió ADR), y —de forma crítica— el **OK explícito antes de crear cualquier recurso facturable** en Azure. El despliegue real fue una compuerta dura que no se cruzó sin su autorización.
- Por política de seguridad, el agente **no** modifica controles de acceso del repositorio (branch protection, App Registration): esos comandos se entregan documentados para que Santiago los ejecute (ver `deploy/terraform/README.md`).
- Los límites y las divergencias respecto al plan original se registraron como ADRs y en `deferred-work.md` en lugar de disimularse. La honestidad sobre lo que funciona (y lo que no) es parte del entregable.
