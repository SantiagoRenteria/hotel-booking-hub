# Story 7.1: Traza distribuida propagada extremo a extremo

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

> **Trazabilidad:** — → **FR-25** → `AC-E7.1.x` · **Diferenciador (recortable · Fase 2)**
> **Porqué:** ante un fallo hay que ver el span exacto; el `trace-id` técnico (W3C) debe atravesar todos los saltos, distinto del id de correlación de negocio. [Source: docs/planning-artifacts/epics.md#Story-7.1]

## Story

Como **operador**,
quiero **seguir una petición por todos los saltos con un `trace-id`**,
para **localizar el span exacto donde algo falla**.

## Acceptance Criteria

> Convención (heredada del doc de épicas): un `Dado/Cuando/Entonces` = una aserción observable; **números, no adjetivos**; identificador de dominio en `código`, mensaje de negocio entre "comillas". [Source: docs/planning-artifacts/epics.md#Convención-de-historias]

**AC-E7.1.1 — Propagación completa por los saltos síncronos (HTTP)**
**Dado** una petición autenticada que entra por el `ApiGateway`
**Cuando** recorre `Gateway (YARP) → servicio (Hoteles.Api / Reservas.Api)`
**Entonces** el mismo `traceparent` (W3C Trace Context) se propaga entre procesos y **todos los spans comparten un único `TraceId`** (32 hex), visible como una sola traza en el dashboard de Aspire.

**AC-E7.1.2 — Span de negocio propio bajo el `ActivitySource` compartido**
**Dado** un comando/consulta que pasa por el pipeline del mediador de un servicio
**Cuando** se procesa
**Entonces** existe **al menos 1 span propio** emitido por el `ActivitySource` compartido `"HotelBookingHub"` (no solo los de instrumentación de ASP.NET Core), y ese span **cuelga del `TraceId` de la petición** (misma traza en el waterfall).

**AC-E7.1.3 — Correlación a través del salto asíncrono (productor → worker), Dapr-ready**
**Dado** un evento de integración emitido por un servicio (p. ej. `ReservaConfirmada.v1`) que el `Notificaciones.Worker` procesa
**Cuando** el worker despacha el evento
**Entonces** el span del consumidor **se correlaciona con la traza originante** mediante el contexto W3C transportado en el envelope/metadatos del outbox (parent o link), de modo que en el dashboard la actividad del worker es **atribuible a la misma petición** que originó el evento.
**Y** el `TraceId` **de negocio** del envelope (`EventoIntegracion.TraceId`) se mantiene como campo de correlación de negocio, **distinto** del `traceparent` técnico (no se confunden). [Source: docs/planning-artifacts/architecture.md#Observabilidad-y-logging]

**AC-E7.1.4 — Span de fallo visible con su excepción**
**Dado** un fallo inducido en un servicio (excepción no controlada en un handler)
**Cuando** abro la traza en el dashboard
**Entonces** el waterfall **marca el span exacto** (servicio + operación) con `status = Error` y la excepción registrada (tipo + mensaje), no un fallo genérico sin ubicación.

**AC-E7.1.5 (negativo) — Health/aliveness fuera de la traza**
**Dado** tráfico de sondas a `/health` y `/alive`
**Cuando** reviso las trazas
**Entonces** **no** generan spans de traza (ya excluidos por el filtro de `AddAspNetCoreInstrumentation`), para no contaminar el waterfall de negocio.

## Alcance y realidad del transporte (LEER ANTES DE IMPLEMENTAR)

⚠️ **Constraint material verificado en el código (no en la teoría):** el transporte **Dapr pub/sub NO está cableado** en el sistema. Está diferido desde E1/E5 de forma deliberada y documentada:

- `Reservas.Infrastructure/Mensajeria/PublicadorEventosLog.cs` y su homólogo en Hoteles son **placeholders** que solo escriben al log ("Mantiene el servicio ejecutable end-to-end sin Dapr; lo reemplaza el transporte real (Dapr pub/sub) más adelante").
- `Notificaciones.Worker/Worker.cs` **solo late** (heartbeat); los consumidores (`ConsumidorReservaConfirmada`, etc.) están registrados en DI y se ejercitan **vía `DespachadorNotificaciones` en tests de integración**, no por una suscripción de transporte real.
- `AppHost/AppHost/AppHost.cs` declara `RabbitMQ` como recurso pero **no** hay sidecars Dapr ni componentes pub/sub; el comentario lo difiere explícitamente.

**Implicación para esta historia (Diferenciador · recortable):** el salto físico literal "`sidecar Dapr → Worker`" del enunciado del épica **no existe como código en ejecución**. Por eso el AC-E7.1.3 se acota a **correlación Dapr-ready a través de la costura asíncrona real que sí existe** (outbox del productor → despacho del worker): se transporta el contexto W3C en el envelope/metadatos y se restaura en el consumidor. Cuando se cablee Dapr (fuera de alcance de esta prueba), la propagación por el sidecar es automática (Dapr propaga `traceparent` en el CloudEvent) y esta costura manual se retira.

**Esto NO es una desviación nueva:** es la realidad establecida y aceptada en E1–E6 (ver retro E6: "la suscripción al transporte real (Dapr pub/sub) sigue diferida"). Se implementa lo demostrable con honestidad de alcance (criterio de evaluación: "alcance honesto — qué corre vs. qué se documenta"). **Si el dev/revisor considera que cablear Dapr real entra en alcance, es una decisión de diseño → detener y activar party-mode** (condición de ancla del protocolo autónomo).

## Tasks / Subtasks

- [ ] **Task 1 — Registrar el `ActivitySource` compartido `"HotelBookingHub"`** (AC: 7.1.2)
  - [ ] Definir una constante única para el nombre del source (evitar el string mágico repetido). Ubicación recomendada: `src/Comun/HotelBookingHub.Comun/` (transversal a los servicios) — p. ej. `Observabilidad/ActividadHotelBookingHub.cs` con `public const string NombreSource = "HotelBookingHub"` y un `static readonly ActivitySource Source`.
  - [ ] En `ServiceDefaults/Extensions.cs::ConfigureOpenTelemetry`, añadir `tracing.AddSource("HotelBookingHub")` **además** del `builder.Environment.ApplicationName` ya presente (hoy solo se registra el ApplicationName → los spans propios no se exportan).
  - [ ] Emitir al menos un span propio en un punto de valor del pipeline (p. ej. un `TracingBehavior` en el pipeline del mediador, o envolver el handler de `CrearReservaCommand`). Mantener el behavior **sin side effects de negocio** (ADR-018), igual que `LoggingBehavior`.
- [ ] **Task 2 — Verificar/asegurar la propagación HTTP Gateway→servicio** (AC: 7.1.1)
  - [ ] Confirmar que YARP reenvía el header `traceparent` y que la instrumentación `AddAspNetCoreInstrumentation` + `AddHttpClientInstrumentation` (ya en ServiceDefaults) continúa el contexto W3C entre procesos. No reinventar: es comportamiento por defecto de OTel; el trabajo es **verificarlo con una traza de ejemplo**, no cablear propagadores a mano.
  - [ ] Si el Gateway debe originar el `traceparent` cuando el cliente no lo envía, documentar/confirmar que `AddAspNetCoreInstrumentation` crea la actividad raíz en el borde.
- [ ] **Task 3 — Correlación a través del salto asíncrono (Dapr-ready)** (AC: 7.1.3)
  - [ ] Capturar el contexto W3C **completo** (`traceparent`) en el momento de encolar al outbox (productor), transportándolo como **metadato del `OutboxMessage`/envelope** — SIN confundirlo con `EventoIntegracion.TraceId` (que es correlación de **negocio**, = `Activity.Current.TraceId`, ver architecture#Observabilidad). Decidir explícitamente: metadato de outbox vs. campo nuevo del envelope. **Si toca el contrato del envelope (`EventoIntegracion`), hay contract tests en `tests/Contracts` que se deben actualizar** — evaluar el costo y preferir metadato de transporte si evita romper el contrato.
  - [ ] En el consumidor (`DespachadorNotificaciones.DespacharAsync` o `IProcesadorEvento`), iniciar la Activity del worker con el contexto restaurado como **parent** (`ActivitySource.StartActivity(nombre, ActivityKind.Consumer, parentContext)`) o como **link** si se prefiere no encadenar. Documentar la elección.
- [ ] **Task 4 — Span de fallo con excepción** (AC: 7.1.4)
  - [ ] Asegurar que una excepción no controlada marca el span con `Error` y registra la excepción (`Activity.SetStatus(ActivityStatusCode.Error)` + `AddException`/tag). La instrumentación de ASP.NET Core ya marca errores HTTP; para el span propio del pipeline, capturarlo en el `TracingBehavior` (re-lanzando, como hace `LoggingBehavior`).
- [ ] **Task 5 — Tests** (AC: todos)
  - [ ] **Unit/Integration:** verificar propagación con un `ActivityListener` en memoria (patrón estándar OTel para tests) — afirmar que los spans comparten `TraceId` y que el span de negocio proviene del source `"HotelBookingHub"`. No requiere dashboard.
  - [ ] **Correlación asíncrona:** test que emite un evento con contexto W3C y afirma que el span del consumidor cuelga del mismo `TraceId` (parent) o lo referencia (link).
  - [ ] **Fallo:** test que induce excepción y afirma `status = Error` + excepción en el span.
  - [ ] **Negativo:** afirmar que `/health` y `/alive` no producen spans (o reutilizar el filtro ya existente y no romperlo).
- [ ] **Task 6 — Evidencia y documentación** (AC: 7.1.1, 7.1.4)
  - [ ] Capturar **una traza de ejemplo** del dashboard de Aspire (waterfall multi-servicio + un span de fallo) como evidencia del diferenciador. Guardar la captura/nota en `docs/` (p. ej. junto a la doc de observabilidad) y referenciarla.
  - [ ] Documentar la relación **`traceId` de negocio vs. `traceparent` técnico** y el estado del transporte Dapr (diferido), para que la ausencia del sidecar se lea como criterio, no como omisión.

## Dev Notes

### Estado actual del código que se toca (leer los archivos, no asumir)

- **`src/AppHost/ServiceDefaults/Extensions.cs`** — `ConfigureOpenTelemetry` ya configura: logs OTel (`IncludeFormattedMessage`, `IncludeScopes`), **métricas** (`AddAspNetCoreInstrumentation` + `AddHttpClientInstrumentation` + `AddRuntimeInstrumentation`), **trazas** (`AddSource(ApplicationName)` + `AddAspNetCoreInstrumentation` con filtro que excluye `/health` y `/alive` + `AddHttpClientInstrumentation`). Exportador OTLP activo **solo si** `OTEL_EXPORTER_OTLP_ENDPOINT` está seteado (Aspire lo inyecta automáticamente en dev → dashboard). **Gap principal: falta `AddSource("HotelBookingHub")`** → los spans de dominio no se exportan hoy.
- **`src/ApiGateway/Program.cs`** — YARP (`AddReverseProxy().LoadFromConfig`), `AddServiceDefaults()` ya aplica OTel. Cadena de middleware: `UseForwardedHeaders → (UseHsts prod) → UseRateLimiter → UseCors → UseAuthentication → UseAuthorization → MapDefaultEndpoints → MapReverseProxy().RequireAuthorization()`. No tocar la seguridad; solo verificar propagación.
- **`src/Servicios/*/*.Api/Program.cs`** — cada servicio aplica `AddServiceDefaults()`; el pipeline del mediador se registra vía `RegistroMediador`. El span propio se emite mejor en un behavior del pipeline (junto a `LoggingBehavior`/`ValidationBehavior` en `HotelBookingHub.Comun/Mensajeria/Behaviors`).
- **`EventoIntegracion`** (`src/Comun/HotelBookingHub.Comun/Eventos/EventoIntegracion.cs`) — record con `TraceId` (string W3C, correlación de **negocio**; nulo si no hay actividad). **No** lleva `traceparent` completo (trace-id + span-id + flags). El salto asíncrono necesita el `traceparent` completo para encadenar como parent → decidir dónde transportarlo (metadato de outbox recomendado, para no romper el contrato).
- **`DespachadorNotificaciones`** (`src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/DespachadorNotificaciones.cs`) — punto único donde el worker procesa cada evento (con tope de intentos + dead-letter). Es el lugar natural para abrir la Activity del consumidor.
- **`LoggingBehavior`** (`src/Comun/HotelBookingHub.Comun/Mensajeria/Behaviors/LoggingBehavior.cs`) — patrón a imitar para un `TracingBehavior`: primer behavior, abre scope, re-lanza en `catch`, sin lógica de negocio. El comentario ya dice "El `TraceId`/`SpanId` los aporta el enricher de OTel".

### Patrones y convenciones de arquitectura a respetar

- **`ActivitySource` compartido `"HotelBookingHub"`** [Source: docs/planning-artifacts/architecture.md#Observabilidad-y-logging, línea "ActivitySource compartido"].
- **`traceId` del envelope = `Activity.Current.TraceId` (W3C)**, generado por el middleware del Gateway y propagado por `Activity` ambient — **no** un `Guid` propio (anti-patrón explícito en architecture#Anti-patrones). Es **distinto** del `traceparent` que Dapr inyectaría en el CloudEvent (negocio vs. técnico): se documenta la relación, no se confunden. [Source: architecture.md#Observabilidad-y-logging]
- **Logging estructurado** (Serilog + OTel sink) con enricher que vuelca `TraceId`/`SpanId` de `Activity.Current`; esquema de propiedades fijo `TraceId`, `SpanId`, `MessageId`, `AggregateId`. Nota: verificar si Serilog ya está cableado o si el logging OTel de ServiceDefaults es la vía actual; **no introducir** un `Guid` de correlación propio.
- **Behaviors sin side effects de negocio** (ADR-018): un `TracingBehavior` solo observa; no altera el `Result<T>` ni el flujo.
- **CAP-9 · FR-25/26 → `AppHost/ServiceDefaults` (OTel)** es el hogar declarado de la observabilidad. [Source: architecture.md#tabla-CAP-ubicación]

### Versiones (Central Package Management — `Directory.Packages.props`)

- `OpenTelemetry.Exporter.OpenTelemetryProtocol` **1.15.3**, `OpenTelemetry.Extensions.Hosting` **1.15.3**, `.Instrumentation.AspNetCore` **1.15.2**, `.Instrumentation.Http` **1.15.1**, `.Instrumentation.Runtime` **1.15.1**. .NET 10 / Aspire 13. **Si se necesita un paquete OTel nuevo, añadir la versión a `Directory.Packages.props`** (CPM: prohibido versionar en el `.csproj`). `System.Diagnostics.DiagnosticSource` (Activity/ActivitySource) viene con el runtime — no requiere paquete extra.

### Testing standards

- Frameworks del repo: **xUnit** + FluentAssertions (ver proyectos `tests/*.UnitTests`, `tests/*.IntegrationTests`, `tests/*.FunctionalTests`). `TreatWarningsAsErrors` está activo → **0 warnings**. Correr `dotnet format` antes de commitear (gate de CI). [Source: retro E6]
- **Patrón de test de trazas sin dashboard:** usar `ActivityListener` (o `TracerProvider` con un exportador en memoria) para capturar `Activity` y afirmar `TraceId`/parent/source/estado. Es el patrón estándar y determinista; **no** depender del dashboard de Aspire para el gate de CI (el dashboard es evidencia manual del diferenciador).
- **Aislamiento crítico:** los tests de concurrencia (G1) y `OutboxFaultInjection` viven en collections xUnit separadas con `DisableParallelization`; **no** mezclar tests de tracing que muten `Activity.Current` ambient con esas collections sin aislar (un `ActivityListener` global puede filtrarse entre tests → registrar/deregistrar en `IDisposable`/`using`). [Source: architecture.md#aislamiento-crítico]
- Test funcional del Gateway: patrón `WebApplicationFactory<Program>` ya usado en `tests/Seguridad.FunctionalTests` (`JwtTestFactory`, `Program` parcial público del Gateway). Reutilizar `TestKit.Auth` para emitir tokens si un test cruza autenticación.

### Project Structure Notes

- **Alineación:** la lógica transversal de observabilidad va en `ServiceDefaults` (config OTel) + `HotelBookingHub.Comun` (constante del `ActivitySource`, `TracingBehavior`). No crear un assembly nuevo.
- **Frontera Comun (regla dura):** un tipo entra a `Comun` solo si es convención de infraestructura transversal (*cómo* se comunican los servicios). El `ActivitySource` compartido y el behavior de tracing califican (transversales, no dominio). **Ningún tipo de dominio** va en `Comun`. [Source: architecture.md#frontera-Comun / AGENTS.md]
- **Conflicto/variance detectado:** el enunciado del épica dice "sidecar Dapr → worker"; el código no tiene Dapr cableado. Resuelto por acotación de alcance (sección "Alcance y realidad del transporte"), no por invención de infraestructura. Registrar en `deferred-work.md` si aún no está: "propagación física por sidecar Dapr — pendiente de cablear el transporte".

### Continuidad con la Épica 6 (retro)

- **Deuda de honestidad:** calificar cada checkbox con su estado **real** (resuelto-por-código / verificado-por-análisis / diferido / documentado), nunca un `[x]` que miente. El Acceptance Auditor del code-review lo detecta. [Source: retro E6]
- **Detalles .NET 10 que muerden:** contrastar comportamiento por defecto (p. ej. propagadores W3C activos por defecto en OTel .NET) con tests reales, no con supuestos.
- **Dependencias listas:** OTel base del ServiceDefaults (E1) + `trace-id` ya presente en el contrato de errores (401/403/429). 6.x no bloquea E7. [Source: retro E6 "Próxima épica (7)"]

### References

- [Source: docs/planning-artifacts/epics.md#Épica-7 / #Story-7.1] — objetivo, AC-E7.1.1/2, etiqueta Diferenciador.
- [Source: docs/planning-artifacts/architecture.md#Observabilidad-y-logging] — `ActivitySource` "HotelBookingHub", `traceId` negocio vs. `traceparent` técnico, Serilog+OTel enricher.
- [Source: docs/planning-artifacts/architecture.md#tabla CAP↔ubicación] — CAP-9 · FR-25/26 → `AppHost/ServiceDefaults`.
- [Source: docs/implementation-artifacts/epic-6-retro-2026-07-10.md] — preparación E7, deuda diferida, lecciones de proceso.
- [Source: src/AppHost/ServiceDefaults/Extensions.cs] — configuración OTel actual (gap: falta `AddSource("HotelBookingHub")`).
- [Source: src/Comun/HotelBookingHub.Comun/Eventos/EventoIntegracion.cs] — envelope con `TraceId` de negocio.

## Dev Agent Record

### Agent Model Used

{{agent_model_name_version}}

### Debug Log References

### Completion Notes List

### File List
