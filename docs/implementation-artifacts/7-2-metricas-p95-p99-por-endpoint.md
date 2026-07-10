# Story 7.2: Métricas p95/p99 por endpoint

Status: ready-for-dev

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

- [ ] **Task 1 — Verificar el histograma de duración por endpoint** (AC: 7.2.1, 7.2.2)
  - [ ] Confirmar que `AddAspNetCoreInstrumentation()` (ya en `ServiceDefaults/Extensions.cs::ConfigureOpenTelemetry.WithMetrics`) emite `http.server.request.duration` con dimensiones `http.route` / `http.request.method` / `http.response.status_code`.
  - [ ] Confirmar que la **ruta** (no la URL cruda con ids) es la dimensión → evita explosión de cardinalidad y hace comparables los percentiles por endpoint. Si algún endpoint no expone plantilla de ruta, ajustarlo.
- [ ] **Task 2 — Percentiles p95/p99 visibles en el dashboard** (AC: 7.2.1)
  - [ ] Verificar en el dashboard de Aspire que el histograma permite leer p95/p99 por endpoint. Si el dashboard requiere `ExplicitBucketBoundaries` o vista de percentiles configurada, ajustarla en la config de métricas OTel (en `ServiceDefaults`, no por servicio).
  - [ ] (Opcional, solo si aporta) un `Meter` de negocio `"HotelBookingHub"` para una métrica de dominio puntual (p. ej. duración del handler de búsqueda). **No** duplicar lo que la instrumentación HTTP ya da; añadir solo si hay una degradación de dominio que el histograma HTTP no capture.
- [ ] **Task 3 — Fuente de tráfico = money test G1** (AC: 7.2.1)
  - [ ] Reutilizar el test de concurrencia G1 (N reservas concurrentes, ya existente en `Reservas.IntegrationTests`) como generador de carga; **no** crear un load test nuevo (k6). Documentar cómo el operador reproduce la observación.
- [ ] **Task 4 — Health/aliveness fuera de la métrica de negocio** (AC: 7.2.3)
  - [ ] Verificar que `/health` y `/alive` no contaminan las series de negocio (ya se excluyen de **trazas**; para **métricas** confirmar el comportamiento y, si aparecen, separarlas por ruta o filtrarlas de forma consistente con el filtro de trazas).
- [ ] **Task 5 — Tests** (AC: 7.2.1, 7.2.2)
  - [ ] Test con `MetricCollector<double>` / lector de métricas en memoria de OTel: afirmar que tras N peticiones a ≥2 endpoints se registran mediciones en `http.server.request.duration` **etiquetadas por ruta**, y que hay muestras suficientes para derivar percentiles. Determinista, sin dashboard.
  - [ ] Test negativo: peticiones a `/health` no aparecen mezcladas en las series de negocio.
- [ ] **Task 6 — Evidencia y documentación** (AC: 7.2.1)
  - [ ] Capturar del dashboard de Aspire el histograma con p95/p99 por endpoint (bajo el tráfico de G1) como evidencia del diferenciador. Guardar en `docs/` y referenciar.
  - [ ] Documentar el alcance honesto: instrumentado + observable; validación bajo carga con k6 **explícitamente fuera de alcance** (decisión Winston), no una omisión.

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

## Dev Agent Record

### Agent Model Used

{{agent_model_name_version}}

### Debug Log References

### Completion Notes List

### File List
