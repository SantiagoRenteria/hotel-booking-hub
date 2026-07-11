---
baseline_commit: cd4104e8b7166ed5a3d8d0a964f2b640209d1fc8
---

# Story T.2: Pulido de entregables — cobertura de pruebas y documentación

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

> **Trazabilidad:** Segundo lote de pulido de entrega (instrucción de Santiago 2026-07-11) → *(entregables transversales, sin FR)* → `AC-ET.2.x` · **Obligatorio (calidad de entrega)**
> **Porqué:** T.1 cerró el DoD, pero el relevamiento posterior detectó (a) cobertura de pruebas sesgada al camino feliz (Postman plano de 8 requests; smoke solo happy-path/Agente), (b) un **bug de routing real del Gateway** que la falta de cobertura enmascaraba, (c) una **contradicción** del CD (implementado auto-apply vs. decidido on-demand, ADR-021), y (d) documentación desactualizada tras cerrar el transporte Dapr de nube (2026-07-11) y sin narrar el flujo BMAD upstream ni los flujos BDD/E2E.

## Story

Como **evaluador de la prueba** (y como responsable del proyecto),
quiero **cobertura de pruebas exhaustiva por servicio, un Gateway sin rutas rotas, un CD coherente con la decisión tomada, y documentación fiel al estado final**,
para **poder ejercer y auditar cada endpoint, confiar en que lo documentado refleja lo construido, y entender cómo se usó la IA en todo el método BMAD, no solo en la construcción**.

## Acceptance Criteria

**AC-ET.2.1 — Colección Postman completa y organizada por servicio**
`postman/hotel-booking-hub.postman_collection.json` reestructurada en **carpetas por servicio** (Gateway · Hoteles · Reservas · Notificaciones/health), con **un request por endpoint real** y **casos negativos** (401 sin token, 403 rol equivocado, 404 recurso inexistente, 409 conflicto rowVersion/concurrencia, 422 idempotencia/validación). Los positivos encadenan variables (`hotelId`→`habitacionId`→`reservaId`). JWT HS256 minteado en pre-request (CryptoJS) para roles **Agente** y **Viajero**. Sigue ejecutándose con **Newman en CI** (job `smoke-compose`) contra el compose, 0 fallos. Payloads reales validados en 8.2 (enums numéricos; documento `^[A-Za-z0-9\-]{4,20}$`; teléfono `^\+?\d{7,15}$`; cancelación `{"iniciador":2,"decision":1}`).

**AC-ET.2.2 — Smoke exhaustivo (no solo camino feliz)**
`deploy/scripts/smoke.sh` cubre **todos los endpoints alcanzables por el Gateway**: además del flujo actual (crear hotel→habitación→reserva→atajo-cancelación), añade **búsqueda de disponibilidad** (`GET /api/v1/habitaciones/disponibles`), **listado** (`GET /api/v1/reservas`) y **detalle** (`GET /api/v1/reservas/{id}`), **flujo de cancelación en dos pasos** (`solicitud-cancelacion` + `cancelacion/resolucion`), **cancelaciones-pendientes**, **idempotencia** (replay del `Idempotency-Key` → 200 con misma reserva), y **casos negativos** (401/403/404). Ejerce **ambos roles** (Agente y Viajero). Sigue verificando la propagación evento→worker por logs. Corre en CI (compose) y en Azure (tras deploy). Idempotente y con reintentos de cold-start (como hoy).

**AC-ET.2.3 — Fix de routing del Gateway: `disponibles` alcanzable**
`src/ApiGateway/appsettings.json`: añadir ruta YARP **específica** `/api/v1/habitaciones/disponibles` → cluster `reservas`, delante del catch-all `/api/v1/habitaciones/{**catch-all}` → `hoteles` (YARP resuelve por especificidad). Resultado: `GET /api/v1/habitaciones/disponibles` por el Gateway llega a Reservas (200), y el resto de `/api/v1/habitaciones/*` sigue en Hoteles. **Regresión cubierta** por smoke + Postman (y, si es viable sin levantar backends, un aserto de configuración de ruteo en `tests/Seguridad.FunctionalTests`).

**AC-ET.2.4 — CD reconciliado a on-demand (coherente con ADR-021)**
`.github/workflows/cd.yml`: **eliminar** el trigger `on: push: branches:[main]` del job `deploy`; el despliegue corre **solo** por `workflow_dispatch` (`accion=deploy`) + `environment: production` (approval). El teardown sigue por `workflow_dispatch accion=destroy`. Comentarios del `cd.yml` actualizados. **Reconciliar** toda la documentación contradictoria para que diga on-demand de forma consistente: `deploy/terraform/README.md` §CD (hoy dice "auto-apply al merge" en ~L7, L91, L139) y `docs/implementation-artifacts/deferred-work.md`, alineados con **ADR-021** y `docs/uso-de-ia.md`. Mergear la release PR #32 a `main` **no** debe desplegar nada por sí solo.

**AC-ET.2.5 — `docs/uso-de-ia.md` narra TODO el método BMAD (no solo construcción)**
Reescribir/extender `docs/uso-de-ia.md` para narrar el flujo BMAD **desde la etapa 1**: analista/investigación de dominio (Mary), PM/PRD (John → `docs/planning-artifacts/prds/.../prd.md`), SPEC (`docs/specs/spec-hotel-booking-hub/SPEC.md`), arquitectura (Winston → `docs/planning-artifacts/architecture.md`), épicas e historias (`epics.md`), sprint planning/status (`sprint-status.yaml`), UX (Sally), además del ciclo por historia ya documentado (create-story→dev-story→code-review→party-mode→correct-course→retrospective). Cada etapa enlaza su **artefacto real** en el repo. Corregir la nota obsoleta que dice que el adaptador Dapr de nube quedó **"diferido"** (fue **implementado el 2026-07-11**; solo resta verificación de runtime en ACA).

**AC-ET.2.6 — `docs/observabilidad.md` actualizado con Dapr**
Actualizar `docs/observabilidad.md`: el transporte Dapr pub/sub **ya está cableado** (`PublicadorEventosDapr` + suscripción Dapr del worker, seleccionables por `TransporteEventos=Dapr`); documentar la **telemetría del sidecar Dapr** y la **propagación de `traceparent`** en el CloudEvent vía Service Bus en el camino de nube; reconciliar/retirar la sección "Alcance honesto: transporte Dapr diferido" (queda como: RabbitMQ local con correlación por el pipeline; Dapr en nube con propagación por sidecar, pendiente de verificación de runtime). No sobre-afirmar: distinguir lo verificado (local) de lo pendiente (runtime nube).

**AC-ET.2.7 — Documento de flujos BDD/E2E (`docs/bdd-y-e2e.md`)**
Nuevo `docs/bdd-y-e2e.md` que consolide: (a) las **convenciones BDD** del proyecto — métodos `Dado_..._cuando_..._entonces_` en el dominio de cancelación, comentarios `// Given/When/Then` en mensajería/observabilidad, bloques ```gherkin``` como AC ejecutables en la historia 3.1; (b) el **mapeo historia↔test BDD** (4.1→`ReservaCancelacionTests`+`SolicitudCancelacionTests`; 4.2→`ReservaResolucionTests`+`ResolverCancelacionTests`; 4.3→`AtajoYVisibilidadCancelacionTests`; 5.1b→`WorkerG3Tests`+`ConsumidorIdempotenteTests`; 9.1→`TransporteRabbitMqTests`; 3.1→`ProyeccionHabitacionConvergenciaTests`+`ProyeccionCatalogoTests`); (c) la **pirámide de test** (unit xUnit / integration Testcontainers SQL·Redis·RabbitMQ / functional WebApplicationFactory / contract / smoke+Newman) con el flujo reserva→evento→notificación explicado como partido por contrato de evento; (d) por qué **no** se usa SpecFlow/Reqnroll/Playwright (API headless: el rol E2E lo cumplen functional+integration+smoke) y la **aplicabilidad de la skill `bmad-qa-generate-e2e-tests`**. Enlazado desde el README y desde `uso-de-ia.md`.

**AC-ET.2.8 — Verificación integral y sin regresiones**
`dotnet build` + `dotnet format --verify-no-changes` verdes; **suite completa de tests verde** (sin regresiones por el cambio del Gateway ni por cualquier toque de código); `docker compose up` + smoke local verde (incluye los nuevos pasos); Newman verde en local/CI. Documentación sin contradicciones internas sobre el disparo del CD ni sobre el estado del transporte Dapr.

## Tasks / Subtasks

- [x] **Task 1 — Fix de routing del Gateway** (AC: ET.2.3, ET.2.8)
  - [x] `src/ApiGateway/appsettings.json`: añadida ruta `habitaciones-disponibles` (`Match.Path = /api/v1/habitaciones/disponibles`, `ClusterId = reservas`) antes del catch-all `habitaciones`. YARP prioriza la ruta específica por especificidad.
  - [x] `appsettings.Development.json` no añade rutas (solo logging) → coherente. Overrides de ACA (`apps.tf` L155-160) solo sobreescriben `Clusters__*__Destinations__d1__Address`, no las Routes → la ruta nueva (→ cluster `reservas`, ya redirigido al app name de ACA) funciona igual en compose y en Azure.
  - [x] Test de regresión `tests/Seguridad.FunctionalTests/RuteoGatewayTests.cs`: asevera la tabla de rutas de YARP vía `IProxyConfigProvider` (`disponibles`→`reservas`, catch-all→`hoteles`). Red→Green verificado (sin la ruta, el 1er test falla). Un test HTTP no distinguiría el cluster (ambos downstream caídos → 5xx).

- [x] **Task 2 — Smoke exhaustivo** (AC: ET.2.2, ET.2.8)
  - [x] `deploy/scripts/smoke.sh` reescrito: helpers `_curl`/`ok`/`expect` con roles (A/V), headers extra y aserción de código exacto; reintento SOLO de transitorios (cold start). Cubre disponibles (Viajero+Agente), listado, detalle, cancelación en dos pasos, cancelaciones-pendientes, idempotencia (replay 200 misma reserva).
  - [x] Negativos con código exacto: 401 (sin token), 403 (Viajero en `SoloAgente`), 404 (reserva inexistente). El smoke falla si no coinciden.
  - [x] Ambos roles: mint de TOKEN (Agente) + TOKEN_VIAJERO. Script hecho **dual-propósito**: `JWT_SIGNING_KEY` (local) o Key Vault (nube); paso de logs del worker guardado (az en nube / hint de docker compose en local).
  - [x] **Verificado local contra el compose**: `docker compose up` → smoke verde end-to-end (health+alive, camino feliz, lecturas, idempotencia, 2 pasos, negativos 401/403/404).

- [x] **Task 3 — Colección Postman completa por servicio** (AC: ET.2.1, ET.2.8)
  - [x] Reestructurada en 3 carpetas (`item` anidado): **Gateway/Health**, **Hoteles** (crear/editar/deshabilitar/habilitar/eliminar hotel + concurrencia 409; crear/editar/deshabilitar/habilitar habitación), **Reservas** (crear; idempotencia 201/200/422; disponibles Viajero+Agente; listar; detalle; solicitud/pendientes/resolución; atajo). 30 requests.
  - [x] Un request por endpoint real + negativos (401/403/404/409/422). Cadena de `rowVersion` e ids con `pm.collectionVariables`; asertos de status + forma. Pre-request de mint JWT (Agente+Viajero) preservado.
  - [x] **Newman local verde**: `32 requests / 43 assertions / 0 fallos` contra el compose. El job `smoke-compose` de `ci.yml` la sigue invocando (mismo path). Dos asertos corregidos tras runtime: detalle usa `reserva.id` (DTO anidado); delete-inexistente usa GUID válido no-cero (el 0-GUID da 400 por validación de `Id`, no 404).

- [x] **Task 4 — Reconciliar CD a on-demand** (AC: ET.2.4)
  - [x] `.github/workflows/cd.yml`: eliminado `push: branches:[main]`; solo `workflow_dispatch` (deploy/destroy). `if:` de ambos jobs simplificados a `inputs.accion`. Comentarios reescritos a on-demand.
  - [x] `deploy/terraform/README.md` §CD y §3 Flujo reescritos a on-demand (comandos OIDC intactos).
  - [x] Reconciliados además `docs/adr/ADR-021-*.md` y `decisions-adr.md` (nota de **reversión** del refinamiento auto-apply de la 8.3), `deferred-work.md`, y nota de reversión en la historia `8-3-*.md`. Grep del repo → sin afirmaciones contradictorias vigentes (solo contexto histórico marcado como tal).

- [x] **Task 5 — `docs/uso-de-ia.md`: método BMAD completo** (AC: ET.2.5)
  - [x] Nuevo §1 "El flujo BMAD de punta a punta": análisis/dominio (Mary), PRD (John → `prd.md`), SPEC (`SPEC.md`), arquitectura (Winston → `architecture.md`), épicas/historias (`epics.md`), sprint planning (`sprint-status.yaml`), UX (Sally, API headless), correct-course (sprint-change-proposals) — cada etapa enlaza su artefacto real. El ciclo por historia pasó a §2; secciones renumeradas.
  - [x] Corregido el "límite conocido" (§5): el adaptador Dapr de nube ya **no** está diferido (implementado; resta verificación runtime). Enlazado `docs/bdd-y-e2e.md` (nav + §2).

- [x] **Task 6 — `docs/observabilidad.md`: Dapr al día** (AC: ET.2.6)
  - [x] Tabla de propagación y sección de alcance reescritas: LOCAL RabbitMQ (correlación por `trace-id` de negocio; sin `traceparent` físico) vs NUBE Dapr (sidecar propaga `traceparent` W3C en el CloudEvent), marcando verificado (local) vs pendiente de runtime (nube). Retirada la premisa obsoleta "transporte Dapr no cableado".

- [x] **Task 7 — `docs/bdd-y-e2e.md` (nuevo)** (AC: ET.2.7)
  - [x] Documento consolidado: convenciones BDD (Gherkin en historias, `Dado_cuando_entonces` en dominio, `// Given/When/Then` en mensajería), mapeo historia↔test, pirámide (unit/integration/functional/contract/smoke+Newman), flujo reserva→evento→notificación partido por contrato de evento, y el porqué de no-SpecFlow/Playwright + aplicabilidad de `bmad-qa-generate-e2e-tests`. Enlazado desde README (árbol + mapa) y `uso-de-ia.md`.

- [x] **Task 8 — Verificación integral** (AC: ET.2.8)
  - [x] `dotnet format --verify-no-changes` (exit 0), `dotnet build -c Release` (0 warnings/0 errores, TreatWarningsAsErrors), suite completa **verde**: ~364 tests no-G1 (incl. `RuteoGatewayTests`) + 3 G1. `docker compose up` + smoke local verde + Newman `32/43/0`. Grep final: sin afirmaciones vigentes de auto-apply ni de "Dapr diferido/no fluye" (solo contexto histórico en stories/evidencia, marcado como tal). File List y Change Log actualizados.

## Dev Notes

### Naturaleza del trabajo
- Mixto **docs + artefactos de prueba + un fix de correctitud** (Gateway). No es TDD clásico; el único código de producción tocado es `appsettings.json` del Gateway (config) — cuidar que los ~450 tests sigan verdes y no introducir regresión de ruteo. Gate: build+format+tests verdes, compose+smoke+Newman verdes, docs sin contradicciones.

### Contexto ya relevado (evita re-descubrir)
- **Inventario de endpoints** (verificado en código): Hoteles `Hoteles.Api/Program.cs` L80-162 (todo `SoloAgente`); Reservas `Reservas.Api/Program.cs` L111-200 (crear/solicitud/resolución/atajo/pendientes/disponibles/listar/detalle; policies `AgenteOViajero`/`SoloAgente`); Notificaciones `Notificaciones.Worker/Program.cs` (worker + `/eventos` Dapr, sin auth, interno); Gateway `ApiGateway/Program.cs` (`RequireAuthorization` global sobre `MapReverseProxy`, rate limit, CORS, HSTS).
- **Bug del Gateway confirmado**: `ApiGateway/appsettings.json` L26-28 manda `/api/v1/habitaciones/**` a `hoteles`, pero `disponibles` vive en Reservas.
- **Contradicción CD confirmada**: `cd.yml` L10-11 (`push: [main]`) contradice ADR-021 ("on-demand"), `uso-de-ia.md` ("John vetó auto-apply") y la decisión de party-mode/memoria. Decisión de Santiago (2026-07-11): **on-demand**.
- **Convención Postman/smoke reales** (de T.1 y 8.2): baseUrl `http://localhost:8080`; enums numéricos (`estado:1`); reserva body en **una sola línea** (curl `-d` recorta saltos); cancelación `{"iniciador":2,"decision":1}`; JWT minteado, no hay endpoint de login.
- **BDD en tests** (verificado): estilo `Dado_cuando_entonces` en `Reservas.UnitTests/Dominio/ReservaCancelacionTests.cs`+`ReservaResolucionTests.cs`; comentarios `// Given/When/Then` en `Notificaciones.*`/`Comun.Web`/`Hoteles` observabilidad; ```gherkin``` como AC en la historia 3.1 materializado en `ProyeccionHabitacionConvergenciaTests` + `ProyeccionCatalogoTests`. NO hay `.feature`, SpecFlow ni Playwright.

### Convenciones a respetar
- YARP: rutas más específicas ganan al catch-all — no reordenar por texto, YARP puntúa especificidad; igual, colocar la ruta específica primero por claridad.
- Postman: JSON válido, no romper el pre-request CryptoJS; los tests `pm.test` deben aseverar status y forma; Newman debe seguir siendo invocado por el job existente.
- Smoke: bash POSIX; helper `req()` con reintento de cold-start; enums numéricos; cuerpos en una línea; no romper la verificación por logs del worker.
- Docs: honestidad de alcance (distinguir verificado local vs pendiente runtime nube); citar `file:line` reales; enlazar ADRs.

### Project Structure Notes
- **Nuevos:** `docs/bdd-y-e2e.md`.
- **Modificados:** `src/ApiGateway/appsettings.json`, `deploy/scripts/smoke.sh` (y quizá `mint-jwt.sh`), `postman/hotel-booking-hub.postman_collection.json`, `.github/workflows/cd.yml`, `deploy/terraform/README.md`, `docs/implementation-artifacts/deferred-work.md`, `docs/uso-de-ia.md`, `docs/observabilidad.md`, `README.md` (enlace al nuevo doc), posiblemente `tests/Seguridad.FunctionalTests/*` (aserto de ruteo).

### References
- [Source: docs/implementation-artifacts/t-1-cerrar-los-entregables-del-enunciado.md] — base de entregables T.1.
- [Source: src/ApiGateway/appsettings.json#ReverseProxy] · [Source: src/Servicios/Reservas/Reservas.Api/Program.cs] · [Source: src/Servicios/Hoteles/Hoteles.Api/Program.cs]
- [Source: .github/workflows/cd.yml] · [Source: deploy/terraform/README.md#CD] · [Source: docs/adr/ADR-021-*.md]
- [Source: deploy/scripts/smoke.sh, deploy/scripts/mint-jwt.sh] · [Source: postman/hotel-booking-hub.postman_collection.json]
- [Source: docs/uso-de-ia.md] · [Source: docs/observabilidad.md] · [Source: docs/implementation-artifacts/deferred-work.md]
- [Source: memoria transporte-eventos-strategy-decision, e8-despliegue-real-cd-decision]

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Amelia / dev-story)

### Debug Log References

- **Gateway (T1):** Red→Green de `RuteoGatewayTests` verificado (sin la ruta, `disponibles→reservas` falla). Overrides de ACA no tocan Routes → aplica en compose y Azure.
- **Smoke (T2):** verde end-to-end contra el compose local (health+alive, camino feliz, disponibles Viajero+Agente, listado, detalle, idempotencia replay 200, cancelación en 2 pasos, pendientes, negativos 401/403/404).
- **Postman/Newman (T3):** `32 requests / 43 assertions / 0 fallos`. Dos asertos corregidos tras runtime: detalle usa `reserva.id` (DTO anidado `ReservaDetalleDto`); delete-inexistente usa GUID válido no-cero (el 0-GUID da 400 por validación `Id NotEmpty`, no 404).
- **Verificación integral (T8):** format exit 0; build Release 0 warnings; ~364 tests no-G1 + 3 G1 verdes.

### Completion Notes List

- **Tasks 1-8 COMPLETAS.** (1) Fix de routing del Gateway: ruta YARP específica `/api/v1/habitaciones/disponibles`→`reservas` + test de regresión `RuteoGatewayTests`. (2) `smoke.sh` reescrito, exhaustivo y dual-propósito (local/Key Vault). (3) Postman reorganizada por servicio (3 carpetas, 30 requests, positivos+negativos 401/403/404/409/422, cadena de rowVersion). (4) CD reconciliado a **on-demand** (cd.yml sin `push:main`) + reconciliación documental completa (README, ADR-021, decisions-adr, deferred-work, nota de reversión en 8-3). (5) `uso-de-ia.md` narra el método BMAD de punta a punta. (6) `observabilidad.md` al día con el transporte Dapr por entorno. (7) nuevo `docs/bdd-y-e2e.md`.
- **Verificado real:** smoke + Newman contra el compose local; suite completa verde; sin contradicciones vigentes en la doc.
- **Hallazgo de correctitud cerrado:** la búsqueda de disponibilidad era **inalcanzable por el Gateway** (404); ahora enruta a Reservas (200), cubierto por test + smoke + Postman.

### File List

**Nuevos:** `tests/Seguridad.FunctionalTests/RuteoGatewayTests.cs`, `docs/bdd-y-e2e.md`, `docs/implementation-artifacts/t-2-pulido-de-entregables-cobertura-y-docs.md`
**Modificados:** `src/ApiGateway/appsettings.json`, `deploy/scripts/smoke.sh`, `postman/hotel-booking-hub.postman_collection.json`, `.github/workflows/cd.yml`, `deploy/terraform/README.md`, `docs/adr/ADR-021-*.md`, `docs/specs/spec-hotel-booking-hub/decisions-adr.md`, `docs/implementation-artifacts/deferred-work.md`, `docs/implementation-artifacts/8-3-cd-on-demand-y-proteccion-main.md`, `docs/uso-de-ia.md`, `docs/observabilidad.md`, `README.md`, `docs/implementation-artifacts/sprint-status.yaml`

## Change Log

| Fecha | Cambio |
|---|---|
| 2026-07-11 | Story T.2 creada (create-story): pulido de entregables — Postman/smoke exhaustivos, fix routing Gateway (disponibles), CD a on-demand, docs (uso-de-ia full BMAD, observabilidad Dapr, bdd-y-e2e). Status → ready-for-dev. |
| 2026-07-11 | Story T.2 (dev-story): Tasks 1-8 completas. Fix Gateway (disponibles→reservas) + RuteoGatewayTests; smoke exhaustivo dual-propósito; Postman por servicio (30 req, Newman 43/0); CD a on-demand + reconciliación documental; docs uso-de-ia (BMAD e2e), observabilidad (Dapr), nuevo bdd-y-e2e. Format+build+suite completa verdes; smoke+Newman verdes. Status → review. |
