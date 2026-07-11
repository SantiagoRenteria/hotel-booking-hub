---
baseline_commit: 1a0f7a153a0003d581d67efa3c56a5c4307543c3
---
# Story 1.7: Idempotencia de creación de reserva (`Idempotency-Key`)

Status: done

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

- [x] **Task 1 — Puerto + store de idempotencia de reserva** (AC: 1.7.1, 1.7.3) ✅ `IAlmacenIdempotenciaReserva` (`ObtenerAsync` + `GuardarSiAusenteAsync` con semántica SETNX) + `RegistroIdempotencia(HuellaCuerpo, RespuestaJson)` en Application/Abstracciones. Adaptador **Redis** (`When.NotExists` + TTL 24h configurable) y **en memoria** (`TryAdd`). Huella = SHA-256 del JSON del `CrearReservaCommand`.
- [x] **Task 2 — Aplicar en el endpoint `POST /api/v1/reservas`** (AC: 1.7.1..1.7.4) ✅ `CrearReservaIdempotente.ManejarAsync`: sin header → flujo actual; hit misma huella → 200 replay del DTO; hit huella distinta → **422** Problem Details; miss → despacha y guarda (`GuardarSiAusente`) solo en éxito. Clave del cliente ≠ `MessageId` del outbox (documentado).
- [x] **Task 3 — Registro DI + config** (AC: 1.7.1) ✅ Factory Redis-si-configurado (`ConnectionStrings:redis`) / memoria en `RegistroInfraestructura`; `IConnectionMultiplexer` dedicado (SETNX). `StackExchange.Redis` añadido a Reservas.Infrastructure.
- [x] **Task 4 — Tests (TDD)** (AC: todos) ✅ Unit del store en memoria (SETNX no-sobrescribe, roundtrip, clave ausente). Funcional con `ISender` fake que **cuenta despachos**: misma clave+cuerpo → 1 despacho + mismo `Id`; misma clave+cuerpo distinto → 422; sin header → 2 despachos. Determinista, sin BD real. Ciclo Red→Green visible.
- [x] **Task 5 — Documentación** (AC: todos) ✅ OpenAPI del endpoint anotado (`.Produces` 201/200 + `.ProducesProblem` 422). El DOCUMENTO-BASE §8.5 ya se reconcilió (el Idempotency-Key deja de estar como "no implementado"). 📄 La nota en el README de entrega se cierra en la Épica T (el README aún no es material de entrega).

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

### Review Findings

_Code review adversarial de 3 capas (Blind · Edge · Auditor), 2026-07-10. 7 patch (2 ALTA), 1 defer, 2 dismiss._

- [x] [Review][Patch][ALTA] Check-then-dispatch → doble creación bajo concurrencia. ✅ Resuelto: rediseñado a **reservar-antes-de-despachar** — `ReservarAsync` (SETNX de marcador "en curso") ANTES de `sender.Send`; solo el ganador despacha; el otro obtiene replay (si ya terminó) o **409 "en proceso"**. Test de concurrencia (2 POST simultáneos → 1 solo despacho) determinista 3/3. [CrearReservaIdempotente.cs, IAlmacenIdempotenciaReserva.cs]
- [x] [Review][Patch][ALTA] Fallo entre despacho y guardado. ✅ Resuelto por el rediseño (la clave se reserva antes de despachar) + `try/catch` que **libera** la clave ante excepción inesperada (no queda "en curso" para siempre). [CrearReservaIdempotente.cs]
- [x] [Review][Patch][MEDIA] Clave no scopeada por usuario. ✅ Resuelto: la key se compone con el sujeto autenticado (`IContextoAgente.AgenteActual`): `{subject}:{clave}` → sin fuga/bloqueo cross-tenant. [CrearReservaIdempotente.cs, Program.cs]
- [x] [Review][Patch][MEDIA] Tests débiles. ✅ Resuelto: `SenderFake` devuelve `Id` distinto por despacho (igualdad de Id = discriminante real); nuevos tests de concurrencia, claves distintas (AC-E1.7.2) y path de fallo (la clave no se quema). 6 funcionales + 4 unit. [IdempotenciaReservaTests.cs]
- [x] [Review][Patch][MEDIA] Sin validación de longitud del header. ✅ Resuelto: clave > 200 chars → 400. [CrearReservaIdempotente.cs]
- [x] [Review][Patch][MEDIA] Store en memoria sin TTL. ✅ Resuelto: respaldado con `MemoryCache` (TTL 24h, evicción automática) + lock para el SETNX; sin crecimiento ilimitado. [AlmacenIdempotenciaReservaEnMemoria.cs]
- [x] [Review][Patch][BAJA] Replay `!` null-forgiving. ✅ Resuelto: si el JSON guardado deserializa null → 409 "reintente" (no `Ok(null)`). [CrearReservaIdempotente.cs]
- [x] [Review][Defer] `IConnectionMultiplexer` de idempotencia no dispuesto explícitamente (singleton, vive lo que la app). Deferred: registrarlo como singleton gestionado por DI al endurecer; sin impacto en runtime normal. [RegistroInfraestructura.cs]

**Dismiss:** header presente-pero-vacío degrada a sin-idempotencia (aceptable: sin clave = sin dedup, no es una clave válida); la huella depende del orden de `Huespedes` (un reintento reenvía el cuerpo idéntico; reordenar el array es semánticamente otro cuerpo → 422 defendible).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Amelia / dev-story, modo autónomo)

### Debug Log References

- TDD Red→Green visible: `test(1.7): … (RED)` (endpoint ignora el header → 2 despachos / 201 en vez de 422) → fase verde (`CrearReservaIdempotente`).
- 446 tests verdes (unit+functional+contracts+integración), 0 warnings, `dotnet format` limpio.

### Completion Notes List

- **Idempotencia de `POST /api/v1/reservas` por header `Idempotency-Key`** (DOCUMENTO-BASE §8.5): reintento con misma clave+cuerpo → replay (200) sin re-despachar; misma clave+cuerpo distinto → 422; sin header → sin cambios. La huella (SHA-256 del comando) detecta la reutilización de clave con otro cuerpo.
- **Store por entorno:** Redis (SETNX `When.NotExists` + TTL) si hay cadena; en memoria (`TryAdd`) si no. Dedup entre instancias solo con Redis (documentado). Clave de idempotencia (cliente) ≠ `MessageId` del outbox (interno).
- **La clave solo se "quema" en éxito:** un 400/409 NO fija la clave (permite reintento legítimo). El dominio de `Reserva` y el pipeline no cambiaron.
- **Test determinista sin SQL:** `ISender` fake que cuenta despachos aísla el comportamiento de idempotencia del endpoint del create real.

### File List

**Nuevos**
- `src/Servicios/Reservas/Reservas.Application/Abstracciones/IAlmacenIdempotenciaReserva.cs`
- `src/Servicios/Reservas/Reservas.Infrastructure/Idempotencia/OpcionesIdempotenciaReserva.cs`
- `src/Servicios/Reservas/Reservas.Infrastructure/Idempotencia/AlmacenIdempotenciaReservaEnMemoria.cs`
- `src/Servicios/Reservas/Reservas.Infrastructure/Idempotencia/AlmacenIdempotenciaReservaRedis.cs`
- `src/Servicios/Reservas/Reservas.Api/CrearReservaIdempotente.cs`
- `tests/Reservas.UnitTests/Idempotencia/AlmacenIdempotenciaReservaEnMemoriaTests.cs`
- `tests/Reservas.FunctionalTests/IdempotenciaReservaTests.cs`

**Modificados**
- `src/Servicios/Reservas/Reservas.Api/Program.cs` (endpoint idempotente + `[FromHeader]` + OpenAPI)
- `src/Servicios/Reservas/Reservas.Infrastructure/RegistroInfraestructura.cs` (registro del store)
- `src/Servicios/Reservas/Reservas.Infrastructure/Reservas.Infrastructure.csproj` (StackExchange.Redis)

## Change Log

| Fecha | Cambio |
|---|---|
| 2026-07-10 | Story 1.7: idempotencia de `POST /api/v1/reservas` por `Idempotency-Key` (DOCUMENTO-BASE §8.5). Store SETNX (Redis) / memoria; replay 200 / 422 en reutilización; sin header sin cambios. TDD Red→Green. 446 tests verdes. Status → review. |
| 2026-07-10 | Code-review adversarial (3 capas): 7 patch aplicados vía agent-dev (2 ALTA: rediseño reservar-antes-de-despachar contra doble creación concurrente + liberación ante fallo; scope por sujeto; validación de longitud; TTL en memoria; guard de null; tests reforzados con concurrencia/claves-distintas/fallo), 1 defer, 2 dismiss. 450 tests verdes (concurrencia determinista 3/3), format limpio. Status → done. |
