# Observabilidad — trazas distribuidas (Épica 7, FR-25)

Documenta la trazabilidad de extremo a extremo del sistema: qué se instrumenta, cómo se propaga el `trace-id`, y qué es demostrable hoy vs. qué queda diferido. Complementa los tests automatizados (prueba en CI) con la guía para observar las trazas en el dashboard de Aspire (evidencia manual).

## Modelo de trazas

- **Base OTel (E1, `ServiceDefaults`):** cada servicio aplica `AddServiceDefaults()`, que configura OpenTelemetry (trazas + métricas + logs). La instrumentación de **ASP.NET Core** y **HttpClient** propaga el contexto **W3C Trace Context** (`traceparent`) entre procesos automáticamente.
- **`ActivitySource` compartido `"HotelBookingHub"`:** los spans de **negocio** se emiten desde este source único (`HotelBookingHub.Comun.Observabilidad.ActividadHotelBookingHub`). `ServiceDefaults` lo registra con `AddSource("HotelBookingHub")` para exportarlo al dashboard. Sin ese registro, los spans de dominio no se exportarían.
- **Span de negocio por petición:** el `TracingBehavior` del pipeline del mediador emite un span nombrado como el comando/consulta (`CrearReservaCommand`, etc.), envolviendo validación + transacción + handler. Un fallo marca ese span con `status = Error` y registra la excepción → el operador ve el **span exacto** del fallo (AC-E7.1.4).

## `trace-id` de negocio vs. `traceparent` técnico

Son dos cosas distintas y **no se confunden** (arquitectura, sección "Observabilidad y logging"):

- **`traceparent` técnico (W3C):** contexto de tracing que propaga el runtime (`Activity` ambient) entre saltos HTTP. Es lo que encadena los spans en una sola traza.
- **`trace-id` de negocio (envelope `EventoIntegracion.TraceId`):** se captura como `Activity.Current?.TraceId` al emitir un evento y viaja en el envelope como **campo de correlación de negocio**. Permite correlacionar el efecto asíncrono (notificación) con la petición que lo originó.

## Propagación por los saltos

| Salto | Mecanismo | Estado |
|---|---|---|
| Cliente → `ApiGateway` (YARP) | Instrumentación ASP.NET Core crea/continúa la actividad raíz | ✅ automático (OTel) |
| `Gateway → Hoteles.Api / Reservas.Api` | YARP reenvía `traceparent`; HttpClient/AspNetCore instrumentation continúa la traza | ✅ automático (OTel) |
| Servicio (pipeline del mediador) | `TracingBehavior` emite span propio desde `"HotelBookingHub"` | ✅ Story 7.1 |
| Servicio → `Notificaciones.Worker` (evento) | Correlación por `trace-id` de negocio del envelope (span de consumidor cuelga de la misma traza) | ✅ Story 7.1 (Dapr-ready) |
| Sidecar Dapr físico | Propagación de `traceparent` en el CloudEvent | ⏸️ **diferido** (transporte Dapr no cableado; ver abajo) |

## Alcance honesto: transporte Dapr diferido

El enunciado de la épica menciona el salto físico "`sidecar Dapr → worker`". En el sistema, **el transporte Dapr pub/sub está diferido** desde E1/E5 (decisión establecida, no omisión):

- El publicador de eventos es un placeholder (`PublicadorEventosLog`) que registra en el log; no hay sidecars Dapr ni componentes pub/sub cableados en el `AppHost`.
- El `Worker` late; sus consumidores se ejercitan vía `DespachadorNotificaciones` (invocación directa con el envelope), no por una suscripción de transporte real.

**Consecuencia:** la correlación asíncrona se implementa de forma **Dapr-ready** sobre la costura que sí existe (productor → outbox → despacho del worker): se transporta el `trace-id` de negocio y el consumidor abre su span bajo la misma traza. Cuando se cablee Dapr, el sidecar propaga el `traceparent` completo (con el span-id padre real) y esta correlación manual se retira. El span-id padre exacto no viaja hoy; la correlación se hace por `trace-id` (suficiente para atribuir la actividad del worker a la petición originante en el dashboard).

## Prueba automatizada (gate de CI)

Verificación determinista con `ActivityListener` en memoria (sin dashboard):

- `tests/Comun.Web.UnitTests/Observabilidad/TracingBehaviorTests.cs` — el behavior emite 1 span desde `"HotelBookingHub"`; un fallo lo marca `Error`.
- `tests/Hoteles.UnitTests/Observabilidad/TrazaPipelineTests.cs` — el pipeline REAL (`AddMediatorPipeline`) emite el span de negocio (prueba el cableado en `RegistroMediador`).
- `tests/Notificaciones.UnitTests/CorrelacionTrazaTests.cs` — el span del consumidor cuelga del mismo `trace-id` del envelope (32 hex o `traceparent` completo), y es tolerante a `trace-id` ausente.

## Métricas de duración p95/p99 por endpoint (Épica 7, FR-26)

- **Histograma por endpoint:** la instrumentación `AddAspNetCoreInstrumentation` de métricas (en `ServiceDefaults`, desde E1) emite `http.server.request.duration` (meter `Microsoft.AspNetCore.Hosting`) con dimensión por **ruta** (`http.route`) + método + código de estado. La dimensión por ruta plantilla (no URL con ids) mantiene la cardinalidad controlada y hace comparables los percentiles entre endpoints.
- **p95/p99:** son percentiles derivados del histograma; el dashboard de Aspire los calcula y muestra por serie (endpoint). No se instrumenta un `Meter` de negocio extra: el histograma HTTP ya cubre FR-26 sin duplicar señal.
- **Alcance (party-mode: Winston):** se **instrumenta y evidencia**; **no** se monta un load test dedicado (k6) para *validar* percentiles bajo carga — sería over-engineering para la prueba. La carga concurrente del **money test G1** (`Reservas.IntegrationTests`) basta como fuente de tráfico.
- **Health fuera de las series de negocio:** las sondas `/health`/`/alive` quedan en su propia serie de ruta, **separable** de los endpoints `api/v1` (no distorsionan los percentiles de negocio).

### Prueba automatizada (gate de CI)

- `tests/Reservas.FunctionalTests/MetricasDuracionTests.cs` — con un `MeterListener` en memoria sobre `http.server.request.duration`, verifica que tras ejercer ≥2 endpoints hay mediciones **segmentadas por ruta** (≥2 rutas de negocio distintas, una de ellas `reservas`) y que las sondas de salud **no** se atribuyen a rutas de negocio. La no-paralelización del ensamblado (`AssemblyInfo.cs`) aísla el listener process-wide de otras clases de test HTTP.

### Reproducir p95/p99 en el dashboard (evidencia manual)

1. Levantar el `AppHost` (ver abajo) y generar tráfico (ejercer endpoints o correr el money test G1).
2. Dashboard de Aspire → pestaña **Metrics** → recurso del servicio → `http.server.request.duration`: ver el histograma por `http.route` y los percentiles (p95/p99) por endpoint; comparar la latencia de la búsqueda vs. otros endpoints (vigilancia de G7/NFR-1).

## Reproducir la traza en el dashboard de Aspire (evidencia manual)

1. `dotnet run --project src/AppHost/AppHost` (requiere Docker para SQL/Redis/RabbitMQ). Aspire inyecta `OTEL_EXPORTER_OTLP_ENDPOINT` y expone el dashboard.
2. Autenticarse en el Gateway y ejercer un flujo (p. ej. crear reserva). El **money test G1** (`Reservas.IntegrationTests`) también sirve como fuente de tráfico.
3. Abrir el dashboard de Aspire → pestaña **Traces**: una petición aparece como una sola traza (mismo `TraceId`) con spans del Gateway, del servicio (incluido el span `CrearReservaCommand` de `"HotelBookingHub"`) y, tras el evento, la actividad del worker correlacionada. Un fallo inducido resalta el span exacto con su excepción.
