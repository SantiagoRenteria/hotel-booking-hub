---
baseline_commit: ea7e14fc3d36b5311f5a9375e17a1746b84821e0
---

# Story T.5: Lectura del catálogo — GET de hoteles y habitaciones

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

> **Trazabilidad:** Detectado al probar (instrucción de Santiago 2026-07-11) → complementa HU1 (gestión del catálogo) → `AC-ET.5.x` · **Mejora de completitud (no obligatorio del enunciado)**
> **Porqué:** el servicio Hoteles hoy expone **solo escritura** (9 endpoints POST/PUT/DELETE); no hay GET para listar ni releer un hotel/habitación. El `rowVersion` solo llega en la respuesta del create/edit, así que editar "a mano" es incómodo (no hay forma de re-leerlo). Un GET del catálogo completa el CRUD y hace el editar natural (GET → PUT). Se mantiene el aislamiento por agente (Story 6.3).

## Story

Como **agente de viajes**,
quiero **consultar (listar y ver el detalle de) mis hoteles y sus habitaciones**,
para **revisar mi catálogo y obtener el `rowVersion` vigente antes de editar, sin depender de la respuesta de una escritura previa**.

## Acceptance Criteria

**AC-ET.5.1 — GET `/api/v1/hoteles` (lista del agente)**
Endpoint `GET /api/v1/hoteles`, política `SoloAgente`. Devuelve **200** con la lista de los hoteles **NO eliminados** del **agente actual** (identidad del token, no del cliente). Cada ítem incluye datos de lectura + `rowVersion` (base64). Un agente **no** ve hoteles de otros (aislamiento 6.3) ni eliminados. Sin token → 401 (borde Gateway); rol Viajero → 403.

**AC-ET.5.2 — GET `/api/v1/hoteles/{id:guid}` (detalle)**
Endpoint `GET /api/v1/hoteles/{id:guid}`, `SoloAgente`. **200** con el detalle del hotel del agente (Id, Nombre, Ciudad, Dirección, Descripción, Estado, `rowVersion`). **404** si no existe, fue eliminado, o pertenece a **otro agente** (invisible por el query filter — no se filtra la existencia de datos ajenos).

**AC-ET.5.3 — GET `/api/v1/hoteles/{hotelId:guid}/habitaciones` (lista por hotel)**
Endpoint `GET /api/v1/hoteles/{hotelId:guid}/habitaciones`, `SoloAgente`. **200** con las habitaciones del hotel indicado, **solo si el hotel pertenece al agente** (si el hotel es ajeno/eliminado/inexistente → **404**, no una lista vacía que revele nada). Cada ítem: Id, HotelId, Tipo, CostoBase, Impuestos, Ubicación, Estado, Capacidad, `rowVersion`.

**AC-ET.5.4 — GET `/api/v1/habitaciones/{id:guid}` (detalle)**
Endpoint `GET /api/v1/habitaciones/{id:guid}`, `SoloAgente`. **200** con el detalle de la habitación **si su hotel pertenece al agente**; **404** si no existe o su hotel es ajeno/eliminado. Ruta con constraint `:guid` para no colisionar con `/api/v1/habitaciones/disponibles` (esa ruta específica va a Reservas por el Gateway; el catch-all `habitaciones` va a Hoteles — ver ADR/routing T.2).

**AC-ET.5.5 — Aislamiento verificado y sin regresiones**
El aislamiento por agente se ejerce en los 4 GET (dueño ve, ajeno no → 404/lista propia). Suite completa verde (sin romper el índice único de unicidad ni la BD compartida del fixture). Postman (carpeta Hoteles) incluye los 4 GET encadenados con los ids creados; el smoke añade al menos GET lista+detalle del hotel y de la habitación creados.

## Tasks / Subtasks

- [x] **Task 1 — Queries + handlers de Hoteles (lectura)** (AC: ET.5.1, ET.5.2)
  - [x] `ListarHotelesDelAgenteQuery : IRequest<Result<IReadOnlyList<HotelVistaDto>>>` + handler. Lee los hoteles del agente (el query filter global ya aísla por `AgentePropietario` y excluye `Eliminado`). Fail-closed: sin identidad → `Result.Prohibido` (403).
  - [x] `ObtenerHotelDetalleQuery(Guid Id) : IRequest<Result<HotelVistaDto>>` + handler. Usa `IHotelRepository.ObtenerAsync(id)` (ya aislado); null → `Result.NoEncontrado` (404).
  - [x] Nuevo DTO de lectura `HotelVistaDto` (Id, Nombre, Ciudad, Direccion, Descripcion, Estado string, RowVersion base64) — `HotelResponseDto` es pobre (sin Direccion/Descripcion). Método `De(Hotel, byte[] rowVersion)`.
  - [x] Lectura vía **read-port dedicado `ILectorCatalogo`** → `LectorCatalogoSql` (NO se tocó el puerto de escritura `IHotelRepository`; separación CQRS limpia, patrón de `ILectorReservasAgente`). Lee `HotelesDbContext.Hoteles` (hereda el filtro global) tracked y mapea el rowVersion desde la shadow property.

- [x] **Task 2 — Queries + handlers de Habitaciones (lectura)** (AC: ET.5.3, ET.5.4)
  - [x] `ListarHabitacionesDeHotelQuery(Guid HotelId)` + handler: **primero** verifica que el hotel pertenece al agente (`IHotelRepository.ObtenerAsync(hotelId)` → null ⇒ 404); si es propio, lista sus habitaciones.
  - [x] `ObtenerHabitacionDetalleQuery(Guid Id)` + handler: obtiene la habitación y **verifica la propiedad del hotel dueño** (la entidad `Habitacion` NO tiene `AgentePropietario` propio; el aislamiento es transitivo por su `HotelId`). Si la habitación no existe, o su hotel es ajeno/eliminado → 404.
  - [x] DTO de lectura de habitación (reusar/enriquecer `HabitacionResponseDto`: ya trae Id, HotelId, Tipo, CostoBase, Impuestos, Ubicacion, Estado, Capacidad, RowVersion — sirve tal cual).
  - [x] Verificar el aislamiento de `Habitacion`: si el `HotelesDbContext` NO tiene query filter por agente sobre `Habitacion`, la verificación DEBE hacerse por el hotel dueño en el handler (no confiar en un filtro inexistente). Confirmar leyendo `HotelesDbContext.OnModelCreating`.

- [x] **Task 3 — Endpoints minimal API** (AC: ET.5.1-5.4)
  - [x] En `Hoteles.Api/Program.cs`: 4 `MapGet` con `ISender` → `sender.Send(query)` → `resultado.ToOkResult()`, `.RequireAuthorization(PoliticasAutorizacion.SoloAgente)`, `.WithTags(...)`. Rutas exactas de los ACs (con `:guid`).
  - [x] Confirmar que el Gateway rutea los GET a Hoteles: `/api/v1/hoteles/**` y `/api/v1/habitaciones/{guid}` van al cluster hoteles; solo `/api/v1/habitaciones/disponibles` va a reservas (ruta específica, T.2). No romper eso.

- [x] **Task 4 — Tests (TDD)** (AC: ET.5.5)
  - [x] Integración (Testcontainers, `Hoteles.IntegrationTests`): lista solo del agente y sin eliminados; detalle dueño→datos+rowVersion; ajeno/eliminado/inexistente→404; habitaciones lista por hotel propio vs hotel ajeno→404; detalle habitación propia vs de hotel ajeno→404. **Usar nombres/datos únicos por test** (campo `Guid`, patrón ya usado) para no chocar con el índice único de unicidad en la BD compartida.
  - [x] Funcional (`Hoteles.FunctionalTests`): Viajero en un GET `SoloAgente` → 403.
  - [x] Red→Green visible (los endpoints no existen → 404/no-ruta antes; verdes tras implementarlos).

- [x] **Task 5 — Artefactos + verificación** (AC: ET.5.5)
  - [x] Postman (carpeta Hoteles): añadir los 4 GET encadenados con `{{hotelId}}`/`{{habitacionId}}`; asertar 200 + forma (lista es array; detalle trae el id pedido y `rowVersion`). Mantener idempotencia por corrida (runId ya cablea nombres únicos).
  - [x] Smoke (`deploy/scripts/smoke.sh`): tras crear hotel/habitación, `GET /api/v1/hoteles`, `GET /api/v1/hoteles/{id}`, `GET /api/v1/hoteles/{id}/habitaciones`, `GET /api/v1/habitaciones/{id}` → 200. Verificar contra el compose y con `newman -n 2` que sigue idempotente.
  - [x] `dotnet build` (0 warnings) + `dotnet format` limpio + suite completa + G1 verdes.

## Dev Notes

### Naturaleza del trabajo
- Feature de **lectura CQRS** (queries + handlers + endpoints), TDD con integración real (Testcontainers) + funcional (auth). Complejidad media; BDD **no** ceremonial (tests convencionales, como 3.2/3.3).

### Contexto verificado (evita re-descubrir)
- **Mediator:** `AddMediatorPipeline(typeof(CrearHotelCommand).Assembly)` escanea el ensamblado de Application → los nuevos query-handlers se **auto-registran** (no hay que registrarlos a mano). Endpoints usan `ISender sender` (patrón de Reservas.Api).
- **Aislamiento hoteles:** `HotelesDbContext` tiene query filter global `!Eliminado && (!_aislamientoActivo || AgentePropietario == _agenteActual)`. `IHotelRepository.ObtenerAsync(id)` ya lo hereda (null si ajeno/eliminado → 404). `IContextoAgente` cableado en Hoteles.Api.
- **Aislamiento habitaciones (GUARDARRAÍL):** confirmar si hay query filter por agente sobre `Habitacion`; probablemente **no** (su aislamiento es transitivo por el hotel). Si no lo hay, el handler DEBE validar la propiedad del hotel dueño antes de devolver la habitación (no asumir filtro). Patrón de Reservas 3.3: identidad server-side, 404 para lo ajeno.
- **DTOs:** `HotelResponseDto` (Id, Nombre, Ciudad, Estado, RowVersion) es de escritura y le faltan Direccion/Descripcion → crear `HotelVistaDto` más rico. `HabitacionResponseDto` ya es completo (incluye HotelId, Capacidad, RowVersion) → reusar.
- **Puertos:** `IHotelRepository`/`IHabitacionRepository` tienen `CrearAsync`/`ObtenerAsync`/`GuardarConcurrenciaAsync`; añadir el listado (o leer vía DbContext en el handler; ambos heredan el filtro). Mapear rowVersion desde la shadow property (`db.Entry(x).Property("RowVersion").CurrentValue`).
- **Result→HTTP:** `ToOkResult()` (200/404/403/409 centralizado). `Result.Prohibido`→403, `Result.NoEncontrado`→404.
- **Routing Gateway (no romper):** `/api/v1/habitaciones/disponibles` (específica)→reservas; catch-all `/api/v1/habitaciones/**` y `/api/v1/hoteles/**`→hoteles (T.2). El GET `/api/v1/habitaciones/{guid}` cae en el catch-all→hoteles ✓.

### Project Structure Notes
- **Nuevos:** slices de query bajo `Hoteles.Application/Hoteles/` (p. ej. `ListarHotelesDelAgente`, `ObtenerHotelDetalle`) y `Hoteles.Application/Habitaciones/` (`ListarHabitacionesDeHotel`, `ObtenerHabitacionDetalle`); `HotelVistaDto`; tests nuevos.
- **Modificados:** `Hoteles.Api/Program.cs` (4 MapGet), `IHotelRepository`/`IHabitacionRepository` (+ listado) y sus adaptadores EF, colección Postman, `deploy/scripts/smoke.sh`.
- **Cuidado:** BD compartida del fixture + índice único de unicidad → datos únicos por test.

### References
- [Source: src/Servicios/Reservas/Reservas.Application/Reservas/{ListarReservasDelAgente,ObtenerReservaDetalle}] — patrón de query de lectura aislada por agente.
- [Source: src/Servicios/Hoteles/Hoteles.Infrastructure/Persistencia/HotelesDbContext.cs#query-filter] · [Source: HotelRepository.cs, HabitacionRepository.cs]
- [Source: src/Servicios/Hoteles/Hoteles.Api/Program.cs] — endpoints + AddMediatorPipeline.
- [Source: docs/implementation-artifacts/6-3-*.md] — aislamiento entre agentes. [Source: memoria e6-hotel-ownership-decision]

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Amelia / dev-story)

### Debug Log References

- **Aislamiento (verificado):** `Habitacion` NO tiene query filter (solo `Hotel`) → el lector verifica la propiedad del hotel dueño (`db.Hoteles.AnyAsync`, filtrado) antes de devolver una habitación. `LecturaCatalogoTests` (4 casos de integración) cubre dueño/ajeno/eliminado/inexistente en hoteles y habitaciones.
- **Suite:** Hoteles.IntegrationTests 32 (28+4), Hoteles.FunctionalTests 3 (2+1: cobertura estructural 9→13 endpoints /api SoloAgente + GET 403). Suite completa + G1 verdes; format limpio.
- **E2E compose:** smoke verde (4 GET + 404); Newman **single run 38 req / 58 asserts / 0**. Nota: `newman -n 2` rápido (76 req) dispara el rate limiter del Gateway (100/60s, feature E6) con 429 — NO es regresión; el uso normal (1 corrida, o 2 espaciadas) queda bajo el límite.

### Completion Notes List

- **Tasks 1-5 COMPLETAS.** 4 endpoints GET (hoteles lista+detalle, habitaciones lista-por-hotel+detalle), SoloAgente, aislados por agente. CQRS: 4 queries+handlers (mediator, auto-registrados por escaneo) sobre un read-port `ILectorCatalogo` → `LectorCatalogoSql`.
- **Desviación de diseño (mejor que lo escrito):** en vez de añadir `ListarAsync` a `IHotelRepository` (puerto de ESCRITURA), la lectura se hizo por un **read-port dedicado `ILectorCatalogo`** (Application.Abstracciones) implementado en Infra — separación CQRS limpia, idéntica al patrón de Reservas (`ILectorReservasAgente`). El puerto de escritura queda intacto.
- **DTO:** `HotelVistaDto` nuevo (incluye Direccion/Descripcion + rowVersion, ausentes en `HotelResponseDto`); habitación reusa `HabitacionResponseDto`.
- **Aislamiento de habitaciones:** transitivo por el hotel dueño (la entidad no tiene identidad de agente) — verificado en el lector y en tests.

### File List

**Nuevos:** `src/Servicios/Hoteles/Hoteles.Application/Hoteles/HotelVistaDto.cs`, `.../Abstracciones/ILectorCatalogo.cs`, `.../Hoteles/ListarHoteles/ListarHotelesDelAgenteQuery.cs`, `.../Hoteles/ObtenerHotel/ObtenerHotelDetalleQuery.cs`, `.../Habitaciones/ListarHabitaciones/ListarHabitacionesDeHotelQuery.cs`, `.../Habitaciones/ObtenerHabitacion/ObtenerHabitacionDetalleQuery.cs`, `src/Servicios/Hoteles/Hoteles.Infrastructure/Persistencia/LectorCatalogoSql.cs`, `tests/Hoteles.IntegrationTests/LecturaCatalogoTests.cs`
**Modificados:** `src/Servicios/Hoteles/Hoteles.Api/Program.cs` (4 MapGet + usings), `.../Hoteles.Infrastructure/RegistroInfraestructura.cs` (DI del lector), `tests/Hoteles.FunctionalTests/CoberturaAutorizacionHotelesTests.cs` (9→13 + GET 403), `postman/hotel-booking-hub.postman_collection.json` (5 GET), `deploy/scripts/smoke.sh` (lecturas GET + 404)

## Change Log

| Fecha | Cambio |
|---|---|
| 2026-07-11 | Story T.5 creada (create-story): GET lectura del catálogo (hoteles lista+detalle, habitaciones lista-por-hotel+detalle), SoloAgente, aislado por agente. Status → ready-for-dev. |
| 2026-07-11 | Story T.5 (dev-story): 4 GET implementados (CQRS con read-port ILectorCatalogo), TDD (LecturaCatalogoTests + GET 403), Postman+smoke con los GET. Suite completa+G1 verdes; smoke+Newman(single) verdes. Status → review. |
| 2026-07-11 | Code-review (3 capas) + follow-ups: fail-closed de identidad en los 4 handlers (+test funcional 403), rate limit holgado en compose (evita 429 en re-runs), comentario de tracking del rowVersion, subtask stale corregido. Re-verificado: suite+G1 verdes, smoke + Newman -n 2 (116 asserts/0). |

## Senior Developer Review (AI)

- **Fecha:** 2026-07-11 · **Resultado:** Changes Requested → **resuelto**.
- **Método:** 3 capas adversariales (Blind Hunter, Edge Case Hunter, Acceptance Auditor). **Sin ALTA; sin fugas de aislamiento.** Los 5 ACs se cumplen (Acceptance Auditor).
- **Confirmado correcto:** aislamiento de habitaciones por verificación del hotel dueño (transitivo, `Habitacion` sin filtro propio); 200 vs 404 uniforme (ajeno/eliminado/inexistente → 404, no lista vacía); rowVersion desde entidades tracked (sin NRE hoy); routing del Gateway intacto (`disponibles`→reservas vs `{guid}`→hoteles); SoloAgente en los 4 endpoints.
- **Action items (resueltos):** ver *Review Follow-ups (AI)*.

## Review Follow-ups (AI)

- [x] [AI-Review][Med] Fail-closed ausente: sin identidad el query filter se desactivaba (los handlers devolvían siempre Ok). → Inyectado `IContextoAgente` en los **4 handlers** de lectura + guard `Prohibido` (403) si `AgenteActual` es null (igual que los handlers de escritura y como pedía la Task 1). Test funcional nuevo: rol Agente sin claim email → GET /api/v1/hoteles → **403** (corta antes de la BD).
- [x] [AI-Review][Med] Sin test de aislamiento por el **camino HTTP** (solo a nivel lector). → El test funcional del fail-closed ejerce el seam HTTP (auth → contexto → handler); el aislamiento con datos se cubre en integración (Testcontainers) porque el `HotelesApiFactory` usa una cadena ficticia (sin BD real). Documentado.
- [x] [AI-Review][Low] Rate limiter (100/60s) daba **429** en re-ejecuciones rápidas (`newman -n 2`, o el usuario re-corriendo). → `RateLimit__PermitLimit` holgado **solo en compose** (dev/test); Azure/ACA mantiene el estricto de appsettings. Verificado: `newman -n 2` → 116 asserts/0.
- [x] [AI-Review][Low] `rowVersion` acoplado al tracking por defecto (NRE latente si se añade `AsNoTracking`). → Comentario defensivo en `LectorCatalogoSql.RowVersion` (requiere tracking; alternativa `EF.Property` si se cambia).
- [x] [AI-Review][Low] Subtask de la historia describía `IHotelRepository.ListarAsync` (no implementado; se usó `ILectorCatalogo`). → Texto del subtask corregido a la solución real (read-port CQRS).
- [ ] [AI-Review][Note] Sin paginación en las listas (carga todo el catálogo del agente, tracked). Aceptable para catálogos pequeños; queda como mejora futura fuera del alcance de T.5.
