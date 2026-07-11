---
baseline_commit: 1a0f7a153a0003d581d67efa3c56a5c4307543c3
---
# Story 1.7: Idempotencia de creación de reserva (`Idempotency-Key`)

Status: in-progress

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

> **Trazabilidad:** auditoría de alineación vs DOCUMENTO-BASE §8.5 (2026-07-10) → **control de doble-envío del cliente** → `AC-E1.7.x` · **Obligatorio (alineación con el base doc)**
> **Porqué:** el DOCUMENTO-BASE §8.5 lista `Idempotency-Key` como control de la condición de carrera "doble clic en reservar": el cliente reintenta el `POST` (red inestable / doble submit) y NO debe crear dos reservas. Complementa —no reemplaza— el anti-overbooking (que protege habitación/fechas, no el doble-submit del MISMO cliente).

## Story

Como **viajero (o su cliente)**,
quiero que **reenviar el mismo `POST /api/v1/reservas` con un `Idempotency-Key` devuelva la reserva ya creada en vez de crear otra**,
para **no duplicar mi reserva ante un reintento de red o un doble clic**.

## Acceptance Criteria

**AC-E1.7.1 — Reintento con la misma clave devuelve la misma reserva**
**Dado** un `POST /api/v1/reservas` con header `Idempotency-Key: K` que creó la reserva `R` (`201`)
**Cuando** se reenvía el MISMO request (misma `K`, mismo cuerpo)
**Entonces** responde con **la misma `R`** (mismo `Id`) y **no** se crea una segunda reserva ni segundas `NochesHabitacion` (`exactamente 1` reserva en BD).

**AC-E1.7.2 — Claves distintas no interfieren**
**Dado** dos requests con `Idempotency-Key` distintas
**Cuando** se procesan
**Entonces** cada uno sigue su curso normal (crea, o `409` por overbooking), sin dedup cruzada.

**AC-E1.7.3 (negativo) — Misma clave, cuerpo distinto → conflicto**
**Dado** una `Idempotency-Key: K` ya usada para un cuerpo
**Cuando** llega otro `POST` con la misma `K` pero **cuerpo distinto**
**Entonces** responde `422` (Problem Details RFC 7807): una clave de idempotencia no puede reutilizarse para otra operación.

**AC-E1.7.4 — Sin header, comportamiento actual intacto**
**Dado** un `POST /api/v1/reservas` **sin** `Idempotency-Key`
**Cuando** se procesa
**Entonces** se comporta exactamente como hoy (crea o `409`), sin regresión.

## Tasks / Subtasks

- [ ] **Task 1 — Puerto + store de idempotencia de reserva** (AC: 1.7.1, 1.7.3)
  - [ ] Definir `IAlmacenIdempotenciaReserva` (Application/Abstracciones): `Task<ResultadoIdempotencia> RecuperarOReservarAsync(string clave, string huellaCuerpo, ct)` y `Task GuardarRespuestaAsync(string clave, string huellaCuerpo, ReservaResponseDto dto, ct)`. La semántica: primera vez con `K` → reserva la clave (marca "en curso"); repetición con misma `K` + misma huella → devuelve la respuesta guardada; misma `K` + huella distinta → señala conflicto.
  - [ ] Adaptador **Redis** (`AlmacenIdempotenciaReservaRedis`, Infrastructure) con **SETNX + TTL** vía `IConnectionMultiplexer` (patrón idéntico al `InboxIdempotenciaRedis` del worker, 5.1b) si hay Redis configurado; **fallback en memoria** (`AlmacenIdempotenciaReservaEnMemoria`) para dev sin Redis (misma política "Redis-si-configurado" del endpoint). TTL configurable (p. ej. 24h).
  - [ ] Guardar la **huella del cuerpo** (hash SHA-256 del JSON canónico del `CrearReservaCommand`) + la respuesta (`ReservaResponseDto`) para replay.
- [ ] **Task 2 — Aplicar en el endpoint `POST /api/v1/reservas`** (AC: 1.7.1..1.7.4)
  - [ ] Leer el header `Idempotency-Key` (opcional). Si **ausente** → flujo actual sin cambios (AC-1.7.4). Si **presente**: calcular la huella del cuerpo; consultar el store: **hit misma huella** → devolver el DTO guardado (200/201 con el mismo `Id`); **hit huella distinta** → `422`; **miss** → despachar el comando, y en éxito guardar (clave → huella + DTO) antes de responder.
  - [ ] La clave del cliente (header) es **distinta** del `MessageId` del outbox (interno) — NO confundirlas (comentario claro).
  - [ ] Mapear el conflicto de clave a Problem Details `422` (coherente con el 400/409 del sistema; `application/problem+json`).
- [ ] **Task 3 — Registro DI + config** (AC: 1.7.1)
  - [ ] Registrar el adaptador (Redis-si-configurado, si no memoria) en `Reservas.Api/Program.cs` o `RegistroInfraestructura`. Reutilizar el `IConnectionMultiplexer`/`cadenaRedis` ya presente si aplica (hoy el endpoint usa `IDistributedCache`; para SETNX atómico se necesita `IConnectionMultiplexer` como el worker).
- [ ] **Task 4 — Tests (TDD)** (AC: todos)
  - [ ] **Unit** del store: reservar-clave nueva → OK; repetir misma clave+huella → devuelve guardado; misma clave+huella distinta → conflicto; TTL/expiración si aplica. (En memoria; determinista.)
  - [ ] **Funcional** (WebApplicationFactory): dos `POST` con la misma `K` y mismo cuerpo → el segundo NO vuelve a despachar el comando (store hit) y devuelve el mismo `Id`; misma `K` + cuerpo distinto → `422`; sin header → pasa al pipeline como hoy. *(Para no depender de SQL real, verificar el corto-circuito del store con un `ISender`/handler fake o afirmando que el segundo request no incrementa las creaciones — evitar exigir Testcontainers SQL salvo que ya esté en el proyecto de integración.)*
  - [ ] Determinismo: si el test usa estado ambient compartido, aislarlo (lección 7.x/9.1).
- [ ] **Task 5 — Documentación** (AC: todos)
  - [ ] Nota en el README/OpenAPI del endpoint: header `Idempotency-Key` soportado. Quitar/actualizar cualquier referencia en el DOCUMENTO-BASE que lo daba por no implementado.

## Dev Notes

### Estado actual del código que se toca

- **Endpoint:** `src/Servicios/Reservas/Reservas.Api/Program.cs:91` — `MapPost("/api/v1/reservas", (CrearReservaCommand comando, ISender sender, ct) => sender.Send(...).ToCreatedResult(...))`. Es el ÚNICO endpoint a tocar. Para leer el header, añadir `HttpRequest`/`HttpContext` o `[FromHeader(Name="Idempotency-Key")] string? clave` a la lambda.
- **Respuesta:** `ReservaResponseDto` (`Reservas.Application/Reservas/CrearReserva/ReservaResponseDto.cs`); `ToCreatedResult` en `Comun.Web/ResultadoHttpExtensions.cs` (Result→HTTP centralizado). El DTO guardado se re-emite en el replay.
- **Patrón de idempotencia a imitar:** `Notificaciones.Worker/Notificaciones/InboxIdempotenciaRedis.cs` (SETNX+TTL vía `IConnectionMultiplexer`, atómico entre instancias) + `InboxIdempotenciaEnMemoria` (fallback). Mismo enfoque, distinto propósito (dedup de EFECTO del consumidor vs dedup del POST del cliente).
- **Redis ya disponible:** `Reservas.Api/Program.cs:57-65` registra `IDistributedCache` (Redis-si-configurado). Para **SETNX atómico** conviene `IConnectionMultiplexer` (como el worker), no `IDistributedCache` (su API no expone SETNX). Registrar `IConnectionMultiplexer` cuando haya `cadenaRedis`, o usar el fallback en memoria.
- **`CrearReservaCommand`** — el cuerpo del POST. La huella = hash del JSON serializado (mismos `JsonSerializerDefaults.Web` del sistema) para detectar "misma clave, cuerpo distinto".

### Arquitectura y convenciones

- **DOCUMENTO-BASE §8.5** (control de carrera "doble clic"); **§8.10 práctica #4** (validación) — el 422 va con Problem Details.
- **Result Pattern + Problem Details RFC 7807** (Comun.Web): el conflicto de clave se mapea a `422` con `application/problem+json` (NO `WriteAsJsonAsync` que fija `application/json` — lección retro E6).
- **Clean Architecture:** el puerto en Application, el adaptador en Infrastructure; el endpoint (Api) orquesta. El dominio de `Reserva` NO cambia.
- **Idempotency-Key ≠ MessageId del outbox:** la clave es del cliente (header, alcance HTTP); el `MessageId` es interno (dedup del evento aguas abajo). Documentar la distinción.
- **UUID v7, DateTimeOffset, español sin tildes** — convenciones del repo (§10).

### Testing standards

- xUnit + FluentAssertions; `TreatWarningsAsErrors` (0 warnings); analizadores xUnit (2031/2020); `dotnet format` antes de commitear.
- Funcional del endpoint: patrón `WebApplicationFactory<Program>` (ya usado en `Reservas.FunctionalTests`, con `ReservasApiFactory` + `TestKit.Auth`). El POST /reservas requiere token `AgenteOViajero`.
- El corto-circuito por store (hit) debe verificarse SIN exigir un create real en SQL cuando sea posible (fake `ISender` o verificación de no-doble-despacho), para mantener el test rápido y determinista.

### Project Structure Notes

- **Nuevos:** `Reservas.Application/Abstracciones/IAlmacenIdempotenciaReserva.cs`; `Reservas.Infrastructure/Idempotencia/AlmacenIdempotenciaReservaRedis.cs` + `...EnMemoria.cs`; tests.
- **Modificados:** `Reservas.Api/Program.cs` (endpoint + DI); posible `RegistroInfraestructura.cs`.
- **Variance:** ninguna respecto al base doc — esta historia CIERRA la brecha §8.5.

### References

- [Source: docs/DOCUMENTO-BASE.md#8.5] — Idempotency-Key como control de doble-envío.
- [Source: docs/planning-artifacts/epics.md#Story-1.7]
- [Source: src/Servicios/Reservas/Reservas.Api/Program.cs:91] — endpoint POST /reservas.
- [Source: src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones/InboxIdempotenciaRedis.cs] — patrón SETNX+TTL a imitar.

## Dev Agent Record

### Agent Model Used

{{agent_model_name_version}}

### Debug Log References

### Completion Notes List

### File List
