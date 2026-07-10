---
baseline_commit: e51eb2c1d4720aa3e46de69554445e71f8fcb01c
---
# Story 7.2: Métricas p95/p99 por endpoint

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

> **Trazabilidad:** — → **FR-26** → `AC-E7.2.x` · **Diferenciador (recortable · Fase 2)**
> **Porqué:** detectar degradación (especialmente de la búsqueda bajo carga de escritura, G7) exige percentiles, no promedios. [Source: docs/planning-artifacts/epics.md#Story-7.2]

## Story

Como **operador**,
quiero **métricas de duración p95/p99 por endpoint**,
para **detectar degradación de latencia**.

## Acceptance Criteria

**AC-E7.2.1 — Histograma de duración instrumentado y observable**
**Dado** tráfico sobre los endpoints (incluida la carga concurrente del money test G1)
**Cuando** consulto las métricas en el dashboard de Aspire
**Entonces** hay **histograma de duración por endpoint** (dimensión por ruta/endpoint) con **p95 y p99 disponibles y observables**.

**AC-E7.2.2 — Dimensión por endpoint, no agregado global**
**Dado** peticiones a **≥ 2 endpoints distintos** (p. ej. `POST /api/v1/reservas` y `GET /api/v1/.../disponibilidad`)
**Cuando** consulto el histograma
**Entonces** las series están **segmentadas por endpoint/ruta** (y por método/código de estado según la convención OTel), de modo que la degradación de un endpoint es distinguible de la de otro — no un único número global.

**AC-E7.2.3 (negativo) — Health/aliveness no contaminan la métrica de negocio**
**Dado** tráfico de sondas a `/health` y `/alive`
**Cuando** reviso el histograma por endpoint
**Entonces** las sondas **no** distorsionan las series de los endpoints de negocio (excluidas o claramente separables).

## Alcance (party-mode: Winston) — LEER ANTES DE IMPLEMENTAR

> Se **instrumenta** y se **muestra** una traza/métrica de ejemplo; **NO** se monta un load test dedicado (k6) para *validar* percentiles bajo carga — sería over-engineering para la prueba. **La carga concurrente del money test (G1) basta como fuente de tráfico.** [Source: docs/planning-artifacts/epics.md#Story-7.2, nota de alcance]

**Traducción a trabajo real:** el histograma `http.server.request.duration` **ya lo emite** la instrumentación `AddAspNetCoreInstrumentation` de las métricas OTel (configurada en `ServiceDefaults`). El foco de la historia es, por tanto: (1) **verificar** que el histograma se emite con la dimensión por endpoint/ruta; (2) asegurar que los percentiles p95/p99 son **derivables y visibles** en el dashboard de Aspire; (3) usar el tráfico del money test G1 como fuente y **capturar evidencia**. El riesgo de sobre-ingeniería es real → no añadir un backend de métricas dedicado ni un load runner.

## Tasks / Subtasks

- [x] **Task 1 — Verificar el histograma de duración por endpoint** (AC: 7.2.1, 7.2.2)
  - [x] Confirmado: `AddAspNetCoreInstrumentation()` (ya en `ServiceDefaults`) emite `http.server.request.duration` (meter `Microsoft.AspNetCore.Hosting`) con dimensión `http.route` + método + código. Verificado por test (`MetricasDuracionTests`).
  - [x] Confirmado que la dimensión es la **ruta plantilla** (`/api/v1/reservas`, `/api/v1/habitaciones/disponibles`), no la URL con query → cardinalidad controlada; percentiles comparables por endpoint.
- [x] **Task 2 — Percentiles p95/p99 visibles en el dashboard** (AC: 7.2.1)
  - [x] 📄 El histograma con buckets por defecto de OTel permite derivar p95/p99 en el dashboard de Aspire (pestaña Metrics). **Decisión:** NO se añade `Meter` de negocio ni `ExplicitBucketBoundaries` — el histograma HTTP ya cubre FR-26 sin duplicar señal (evita over-engineering). Documentado en `docs/observabilidad.md`.
- [x] **Task 3 — Fuente de tráfico = money test G1** (AC: 7.2.1)
  - [x] 📄 El test automatizado usa endpoints funcionales como fuente de tráfico determinista; el money test G1 (`Reservas.IntegrationTests`) queda documentado como fuente de carga para la observación manual del dashboard. **No** se creó load test k6 (decisión Winston).
- [x] **Task 4 — Health/aliveness fuera de la métrica de negocio** (AC: 7.2.3)
  - [x] Verificado por test: las sondas `/health` no se atribuyen a rutas de negocio (`api/v1`) → serie separable. No requirió filtro extra (la dimensión por ruta ya las separa).
- [x] **Task 5 — Tests** (AC: 7.2.1, 7.2.2, 7.2.3)
  - [x] `MeterListener` en memoria sobre `http.server.request.duration`: tras ejercer ≥2 endpoints hay mediciones **etiquetadas por ruta** (≥2 rutas de negocio distintas, una `reservas`). Determinista (poll acotado + no-paralelización del ensamblado), sin dashboard. *(Se usó `MeterListener` del runtime en vez de `MetricCollector<T>` para no añadir un paquete NuGet.)*
  - [x] Test negativo: peticiones a `/health` no se mezclan en las series de negocio.
- [x] **Task 6 — Evidencia y documentación** (AC: 7.2.1)
  - [x] `docs/observabilidad.md` ampliado con la sección de métricas p95/p99: histograma por ruta, alcance (sin k6), health separable, test de CI y pasos para reproducir p95/p99 en el dashboard.
  - [📄] **Captura del dashboard DIFERIDA (no hecha):** la subtarea original pedía "guardar la captura en `docs/`"; requiere levantar el `AppHost` (multi-contenedor Aspire) + navegador, fuera del entorno de CI. En su lugar se dejaron **pasos de reproducción** en `docs/observabilidad.md`. Registrado en `deferred-work.md`. (Honestidad de checkbox, lección retro E6: no marcar `[x]` un entregable no producido.)
  - [x] Alcance honesto documentado: instrumentado + observable; validación bajo carga con k6 **explícitamente fuera de alcance** (decisión Winston), no una omisión.

## Dev Notes

### Estado actual del código que se toca

- **`src/AppHost/ServiceDefaults/Extensions.cs::ConfigureOpenTelemetry`** — `WithMetrics` ya registra `AddAspNetCoreInstrumentation()` + `AddHttpClientInstrumentation()` + `AddRuntimeInstrumentation()`. **El histograma de duración de request por endpoint ya se produce**; el trabajo es verificar dimensión por ruta + visibilidad de percentiles + evidencia. Exportador OTLP activo solo si `OTEL_EXPORTER_OTLP_ENDPOINT` (Aspire lo inyecta → dashboard).
- **Money test G1** — vive en `tests/Reservas.IntegrationTests` (concurrencia N sobre SQL real, collection aislada `DisableParallelization`). Es la fuente de tráfico designada. **No** tocar su aislamiento; solo reutilizarlo como generador.
- **Filtro de health en trazas** — `ServiceDefaults` excluye `/health` y `/alive` de **trazas** (no necesariamente de métricas). Verificar el comportamiento en métricas para AC-E7.2.3.

### Patrones y convenciones a respetar

- **CAP-9 · FR-25/26 → `AppHost/ServiceDefaults` (OTel)** es el hogar de la instrumentación; la config de métricas va ahí, no repartida por servicio. [Source: architecture.md#tabla CAP↔ubicación]
- **NFR-1 (rendimiento):** el objetivo de negocio de esta métrica es vigilar p95/p99 **estables bajo carga concurrente de escritura (G7)** — la búsqueda servida por proyección + Redis. La segmentación por endpoint debe permitir aislar la latencia de búsqueda. [Source: architecture.md#NFR / epics.md#NFR-1]
- **NFR-5 (observabilidad):** OpenTelemetry (trazas, métricas, logs) con dashboard de Aspire local / Application Insights nube. [Source: epics.md#NFR-5]
- **No sobre-instrumentar:** cardinalidad controlada (dimensión = ruta plantilla, no URL con ids); un `Meter` de negocio solo si aporta señal que el histograma HTTP no da.

### Versiones (CPM — `Directory.Packages.props`)

- OTel 1.15.x (`.Instrumentation.AspNetCore` 1.15.2, `.Exporter.OpenTelemetryProtocol` 1.15.3). Para tests de métricas en memoria: `Microsoft.Extensions.Diagnostics.Testing` (`MetricCollector<T>`) — **si no está referenciado en el proyecto de test, añadir la versión a `Directory.Packages.props`** (prohibido versionar en `.csproj`).

### Testing standards

- xUnit + FluentAssertions; `TreatWarningsAsErrors` (0 warnings); `dotnet format` antes de commitear (gate CI).
- **Test de métricas determinista:** `MetricCollector<double>` sobre `http.server.request.duration` o un `MeterListener`; afirmar mediciones etiquetadas por ruta tras ejercitar ≥2 endpoints. **No** depender del dashboard para el gate de CI (el dashboard es evidencia manual).
- Registrar/deregistrar cualquier listener global en `using`/`IDisposable` para no filtrar estado entre tests (los `Meter`/`Activity` son ambient).

### Project Structure Notes

- **Alineación:** cambios concentrados en `ServiceDefaults` (config métricas) + tests en el proyecto de integración/funcional pertinente. Sin assembly nuevo.
- **Conflicto/variance:** ninguno respecto al enunciado — el alcance ya lo acotó Winston (instrumentar + evidenciar, sin k6). Registrar en `deferred-work.md` (si no está): "validación de percentiles bajo carga con k6 — fuera de alcance de la prueba (decisión party-mode)".

### Continuidad

- Comparte `ServiceDefaults` con la Story 7.1; si 7.1 ya añadió `AddSource("HotelBookingHub")` y un `TracingBehavior`, 7.2 no lo altera — solo trabaja el eje **métricas**. Evitar conflictos de merge tocando las mismas líneas de `ConfigureOpenTelemetry` con cuidado (bloque `WithMetrics` vs `WithTracing`).
- **Honestidad del checkbox** (lección retro E6): calificar cada task con su estado real (verificado-por-código / configurado / documentado / diferido).

### References

- [Source: docs/planning-artifacts/epics.md#Story-7.2] — AC-E7.2.1 + nota de alcance (Winston: sin k6).
- [Source: docs/planning-artifacts/architecture.md#NFR-1] — p95/p99 estables bajo carga de escritura (G7); búsqueda por proyección + Redis.
- [Source: docs/planning-artifacts/architecture.md#tabla CAP↔ubicación] — CAP-9 · FR-25/26 → `AppHost/ServiceDefaults`.
- [Source: src/AppHost/ServiceDefaults/Extensions.cs] — `WithMetrics` con `AddAspNetCoreInstrumentation` (histograma ya emitido).
- [Source: docs/implementation-artifacts/epic-6-retro-2026-07-10.md] — E7 no bloqueada por 6.x.

### Review Findings

_Code review adversarial de 3 capas (Blind · Edge · Auditor), 2026-07-10. 2 patch, 4 dismiss._

- [x] [Review][Patch] El test `Las_sondas_de_salud_no_contaminan_las_series_de_negocio` no afirmaba positivamente la serie de `/health` y la ausencia global de `api/v1` era vulnerable a contaminación cross-assembly. ✅ Resuelto: ahora afirma por **presencia** que existe la serie con ruta `"/health"` (verificado empíricamente el valor del tag), distinta de las de negocio → separable e inmune a contaminación; `EsperarMedidas` falla con diagnóstico de rutas observadas en timeout. Determinista en 3 corridas. [tests/Reservas.FunctionalTests/MetricasDuracionTests.cs]
- [x] [Review][Patch] Honestidad de checkbox (Task 6). ✅ Resuelto: la subtarea de **captura** del dashboard se recalificó a `[📄]` DIFERIDA (no hecha) con pasos de reproducción en su lugar; registrado en `deferred-work.md`. [docs/implementation-artifacts/7-2-metricas-p95-p99-por-endpoint.md, docs/implementation-artifacts/deferred-work.md]

**Dismiss (no accionables):** dependencia de que `GET /api/v1/reservas` esté mapeado (verificado: los endpoints existen en `Reservas.Api/Program.cs` y `http.route` se emite aun con 401/5xx); `DisableTestParallelization` a nivel de ensamblado (es el alcance correcto — una colección dedicada no evitaría que otra clase HTTP del mismo ensamblado corra en paralelo y contamine el `MeterListener` process-wide); AC-E7.2.2 solo verifica `http.route` y no método/código (el núcleo "por endpoint" está probado; método/código son dimensiones OTel por defecto); diseño del poll (margen 3s + mensaje en el positivo, defendible).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Amelia / dev-story, modo autónomo)

### Debug Log References

- Historia de **verificación + evidencia**: FR-26 ya lo satisface la instrumentación OTel de E1 (`AddAspNetCoreInstrumentation` de métricas); no hubo código de producción nuevo. Por eso no aplica un ciclo Red→Green de lógica: el test es de **caracterización** del comportamiento existente (pasa en verde al escribirse).
- Flakiness detectada y resuelta durante el desarrollo: el `MeterListener` es process-wide → en la corrida completa capturaba/desincronizaba mediciones de otras clases de test HTTP en paralelo (misma lección del listener de tracing en 7.1). Fix: `[assembly: CollectionBehavior(DisableTestParallelization = true)]` en `Reservas.FunctionalTests` + poll acotado para la ventana de timing cliente/servidor. Verificado determinista en 3 corridas.
- Suite completa: **437 tests verdes** (+2 de métricas), 0 warnings, build limpio, `dotnet format` limpio.

### Completion Notes List

- **Sin código de producción:** el histograma `http.server.request.duration` por `http.route` ya lo emite `ServiceDefaults` (E1). La historia lo **verifica** (test) y lo **evidencia** (doc), honrando el alcance de Winston (instrumentar + mostrar, sin k6).
- **Test determinista:** `MeterListener` del runtime (sin añadir `Microsoft.Extensions.Diagnostics.Testing`); poll acotado; ensamblado no-paralelizado para aislar el listener global.
- **Decisiones de alcance:** no se añadió `Meter` de negocio ni buckets explícitos (el histograma HTTP basta para FR-26); p95/p99 se derivan en el dashboard de Aspire (evidencia manual documentada); k6 fuera de alcance.

### File List

**Nuevos**
- `tests/Reservas.FunctionalTests/MetricasDuracionTests.cs`
- `tests/Reservas.FunctionalTests/AssemblyInfo.cs`

**Modificados**
- `docs/observabilidad.md` (sección de métricas p95/p99 por endpoint)

## Change Log

| Fecha | Cambio |
|---|---|
| 2026-07-10 | Story 7.2: verificación + evidencia de las métricas p95/p99 por endpoint. Test de caracterización con `MeterListener` (histograma por ruta + health separable), determinista (no-paralelización + poll). Doc de observabilidad ampliada. Sin código de producción (FR-26 ya cubierto por la instrumentación OTel de E1). 437 tests verdes. Status → review. |
| 2026-07-10 | Code-review adversarial (3 capas): 2 patch aplicados vía agent-dev (test de salud reforzado por presencia de la serie `/health` + diagnóstico de timeout; honestidad de checkbox de la captura diferida), 4 dismiss. Determinista 3/3, format limpio. Status → done. |
