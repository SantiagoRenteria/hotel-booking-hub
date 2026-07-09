---
baseline_commit: 1f7c4b0a6afd31b0d85635a992090c3778a6e466
---

# Story 2.2: Editar hotel y eliminarlo lógicamente (soft delete)

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

Como **agente**,
quiero **editar los datos de un hotel y darlo de baja lógicamente**,
para **mantener el catálogo al día sin perder historial**.

> **Depende de 2.1** (aggregate `Hotel`, `HotelesDbContext` con `rowversion`, slice/pipeline). Aquí se añaden los slices `EditarHotel` y `EliminarHotel` (soft delete), el mapeo `Result→200/204` en `Comun.Web`, y —**saldando el diferido de 2.1**— la **`ExcepcionNegocio` base + exception-handler transversal** para mapear la **concurrencia optimista (rowVersion) a 409**. NO toca habitaciones (2.4) ni eventos (2.5).

## Acceptance Criteria

1. **AC-E2.2.1 — Edición independiente.** Dado un hotel existente, cuando edito sus datos con `EditarHotelCommand` (incluye `rowVersion`), entonces responde `200` con los datos actualizados; las habitaciones del hotel no se alteran.
2. **AC-E2.2.2 — Soft delete.** Dado un hotel activo, cuando lo elimino (`EliminarHotelCommand`), entonces queda marcado `Eliminado` (sin borrado físico) y deja de aparecer en consultas (query filter) y de ofertar; responde `204`.
3. **AC-E2.2.3 — Edición concurrente (concurrencia optimista).** Dado dos agentes que editan el mismo hotel con el mismo `rowVersion`, cuando ambos guardan, entonces `exactamente 1` confirma y el otro recibe `409` (con detalle "recargar y reintentar"), NUNCA `500`. *(Test de integración con Testcontainers.MsSql — conflicto real de `rowversion`.)*
4. **AC-E2.2.4 — No encontrado / ya eliminado.** Dado un `Id` inexistente o un hotel ya eliminado, cuando edito o elimino, entonces responde `404` (Problem Details), sin efecto.

## Tasks / Subtasks

- [x] **Task 1 — Dominio: mutación y soft delete (AC: 1, 2)**
  - [x] `Hotel`: métodos `Editar(nombre, ciudad, direccion, descripcion)` y `Eliminar()` (marca `Eliminado = true`); invariantes en el dominio. *(Nota: `Editar` NO cambia `Estado` — decisión party-mode, la transición se reserva a 2.3.)*
  - [x] Propiedad `Eliminado` (bool) + query filter global `HasQueryFilter(h => !h.Eliminado)` en `HotelesDbContext` + migración (`AgregaEliminadoHotel`)
  - [x] `IHotelRepository`: `ObtenerAsync(Guid, ct)` + `GuardarConcurrenciaAsync(hotel, rowVersionOriginal, ct)` que fija el `rowVersion` del cliente como `OriginalValue` y fuerza el UPDATE (`State=Modified`, excluyendo la shadow `Seq`) para arbitrar el token incluso en ediciones idempotentes
- [x] **Task 2 — Slices `EditarHotel` / `EliminarHotel` (AC: 1, 2, 4)**
  - [x] `EditarHotelCommand(Id, RowVersion, Nombre, Ciudad, Direccion, Descripcion)` → `Result<HotelResponseDto>`; handler: obtiene (404 si null), edita, guarda; concurrencia → `ConflictoConcurrenciaException` → 409
  - [x] `EliminarHotelCommand(Id, RowVersion)` → `Result`; handler: obtiene (404), `Eliminar()`, guarda → 204
  - [x] Validators (id + rowVersion presentes; longitudes vía `LongitudesHotel`, fuente única compartida con el mapeo EF)
- [x] **Task 3 — Transversal: excepción de negocio + mapeo Result→HTTP (AC: 3)**
  - [x] **`ExcepcionNegocio` abstract en `Comun`** portando un `EstadoResultado` (semántica, no HTTP) — **Alternativa C del party-mode**; `HabitacionNoDisponibleException` (Reservas) hereda de ella conservando el 409
  - [x] Handler transversal `ManejadorExcepcionesNegocio` en `Comun.Web` (`IExceptionHandler`) que mapea `ExcepcionNegocio` → Problem Details reutilizando la tabla única `CodigoHttp`; registrado por ambos `*.Api` vía `AddManejoExcepcionesNegocio()`. Borrado el de `Reservas.Api`
  - [x] `Result<T>.ToOkResult()` y `Result.ToNoContentResult()` en `Comun.Web` (200/204/400/404/409)
  - [x] `ConflictoConcurrenciaException : ExcepcionNegocio` (409) lanzada al capturar `DbUpdateConcurrencyException`
- [x] **Task 4 — Endpoint + wiring (AC: 1, 2, 4)**
  - [x] `PUT /api/v1/hoteles/{id:guid}` (editar) + `DELETE /api/v1/hoteles/{id:guid}` (soft delete) en `Hoteles.Api`; `Program.cs` registra el handler transversal y `UseExceptionHandler()`
- [x] **Task 5 — Tests (AC: 1, 2, 3, 4)**
  - [x] Unit: editar happy (200 + nuevo rowVersion), eliminar (204), 404 (inexistente/ya eliminado), 409 (propagación); mapeo Result→HTTP (200/204/404/409); batería del handler transversal (incl. no-negocio→500) en `Comun.Web.UnitTests`
  - [x] **Integración (Testcontainers):** concurrencia optimista real (1 confirma / 1 `409`, nunca 500); no-op con rowVersion obsoleto → 409; soft delete desaparece del query filter (sin borrado físico); borde 404-previo vs 409-carrera edición-vs-borrado
- [x] **Task 6 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Concurrencia optimista (fuente ADR-017 · `rowversion`)

- `Hotel` ya tiene `rowversion` (2.1). El `EditarHotelCommand` transporta el `rowVersion` que el cliente leyó; el handler lo fija como `OriginalValue` de la shadow property `RowVersion` antes de `SaveChanges`. Si otro editó en medio, EF lanza `DbUpdateConcurrencyException` → traducir a `ConflictoConcurrenciaException` (409). NUNCA 500.
- El `rowVersion` viaja como base64 en el DTO/JSON (convención de `patterns.md`).

### Excepción de negocio transversal (party-mode de 2.1, ahora aterriza)

- Crear `ExcepcionNegocio` base en `Comun` (con `CodigoHttp`); el handler transversal en `Comun.Web` la mapea a Problem Details. `HabitacionNoDisponibleException` (Reservas.Domain) hereda de ella → **refactor de código ya implementado (Reservas) → invocar party-mode** para confirmar la forma (¿código en la excepción vs mapa en el handler?) antes de tocar Reservas.

### Límites de alcance

- NO habilitar/deshabilitar como operación dedicada (2.3), NO habitaciones (2.4), NO eventos de catálogo/outbox (2.5). El soft-delete NO cancela reservas existentes (supuesto de negocio; compensación fuera de alcance).

### Anti-patrones a evitar

- Borrado físico (debe ser soft delete + query filter).
- Mapear la concurrencia a 500 (debe ser 409 determinístico).
- Editar habitaciones al editar el hotel (independencia).
- Duplicar el exception-handler por Api (transversal en `Comun.Web`).

### Testing

- Unit en `Hoteles.UnitTests`. **Integración en un nuevo `Hoteles.IntegrationTests`** (Testcontainers.MsSql, patrón de `Reservas.IntegrationTests`): el test de concurrencia carga el hotel en dos contextos con el mismo `rowVersion`, guarda ambos, y verifica 1 OK + 1 conflicto (409) + estado final consistente.

### Previous story intelligence (2.1 / E1)

- De 2.1: `Hotel`, `HotelesDbContext` (rowversion ya mapeado), `HotelRepository`, `AddHotelesInfrastructure`, `Comun.Web` (`ToCreatedResult`). De E1/1.6b: patrón de clasificación de `SqlException`/`DbUpdateException` y de exception→Problem Details.
- **Saldar aquí** el diferido de 2.1: exception-handler base transversal.

### Git

- Commit + push a `develop`; autor **Santiago Renteria**, sin coautoría IA.

### Project Structure Notes

- `Hoteles.Domain/Hoteles/Hotel.cs` (métodos + `Eliminado`), `Hoteles.Application/Hoteles/{EditarHotel,EliminarHotel}/`, `Hoteles.Infrastructure/Persistencia/` (query filter + migración + repo), `Comun/` (`ExcepcionNegocio`), `Comun.Web/` (handler + `ToOkResult`/`ToNoContentResult`), `Hoteles.Api/` (endpoints). Tests en `tests/Hoteles.UnitTests/` + nuevo `tests/Hoteles.IntegrationTests/`.

### References

- [epics.md — Story 2.2](../planning-artifacts/epics.md) (AC-E2.2.1…3).
- [architecture.md — ADR-017 / Result→HTTP / exception→Problem Details](../planning-artifacts/architecture.md).
- Stories 2.1 (base Hoteles), 1.6b (clasificación/exception mapping), 1.5 (Testcontainers).
- [deferred-work.md](deferred-work.md) — exception-handler base transversal (diferido de 2.1).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (modo autónomo).

### Debug Log References

- **Regresión atrapada por el motor real:** el fix del no-op (`entrada.State = EntityState.Modified` para forzar el UPDATE y arbitrar el token en ediciones idempotentes) marcaba también la shadow `Seq` (identity clustered) → `SqlException: Cannot update identity column 'Seq'` en los 5 tests de integración. Resuelto con `entrada.Property("Seq").IsModified = false`. Los unit tests (fake) no lo detectaron; solo Testcontainers. Reafirma la lección de E1.
- Migración `AgregaEliminadoHotel` formateada con `dotnet format` (CRLF/BOM).

### Completion Notes List

- **Party-mode #1 (Alternativa C):** `ExcepcionNegocio` abstract en `Comun` portando `EstadoResultado`; handler transversal en `Comun.Web` reutiliza la tabla única `CodigoHttp`. Rechazadas A (HTTP crudo en `Comun`, web-agnóstico) y B (handler acoplado a subtipos de cada BC). `HabitacionNoDisponibleException` heredó de la base sin romper el 409 (money-test de E1 intacto: Reservas integración 11/11).
- **Party-mode #2 (refinamientos de contrato del code review):** (1) `RowVersion` (base64) se expone en `HotelResponseDto` (201 y 200) para permitir editar sin re-leer; (2) `Estado` sale del PUT — la transición de ciclo de vida se reserva a 2.3; (3) el token sigue en el body por ahora, con migración a `ETag`/`If-Match` diferida a E3 (registrada en `deferred-work.md`).
- **Code review adversarial (3 capas):** hallazgo unánime del *no-op update evade la concurrencia* (edición idempotente con rowVersion obsoleto devolvía 200 en vez de 409) → corregido y cubierto con test de integración dedicado. Resto verificado correcto por las tres capas.
- **DRY:** `LongitudesHotel` (dominio) como fuente única de longitudes para validators (Crear+Editar) y `HasMaxLength` de EF; `HotelResponseDto` promovido a `Hoteles.Application.Hoteles` compartido por los slices.
- **Gates de calidad finales:** build 0/0, `dotnet format` limpio. Tests: Hoteles unit 31/31, Comun.Web 11/11, Reservas unit 51/51, Hoteles integración 5/5, Reservas integración 11/11 (**109 total**).

### File List

**Nuevos — Comun/Comun.Web:**
- `src/Comun/HotelBookingHub.Comun/Excepciones/ExcepcionNegocio.cs`
- `src/Comun/HotelBookingHub.Comun/Excepciones/ConflictoConcurrenciaException.cs`
- `src/Comun/HotelBookingHub.Comun.Web/ManejadorExcepcionesNegocio.cs`
- `src/Comun/HotelBookingHub.Comun.Web/RegistroExcepcionesNegocio.cs`

**Nuevos — Hoteles:**
- `src/Servicios/Hoteles/Hoteles.Domain/Hoteles/LongitudesHotel.cs`
- `src/Servicios/Hoteles/Hoteles.Application/Hoteles/HotelResponseDto.cs`
- `src/Servicios/Hoteles/Hoteles.Application/Hoteles/EditarHotel/{EditarHotelCommand,EditarHotelCommandHandler,EditarHotelCommandValidator}.cs`
- `src/Servicios/Hoteles/Hoteles.Application/Hoteles/EliminarHotel/{EliminarHotelCommand,EliminarHotelCommandHandler,EliminarHotelCommandValidator}.cs`
- `src/Servicios/Hoteles/Hoteles.Infrastructure/Migraciones/20260709042631_AgregaEliminadoHotel*.cs`

**Modificados:**
- `src/Comun/HotelBookingHub.Comun.Web/ResultadoHttpExtensions.cs` (`CodigoHttp` tabla única + `ToOkResult`/`ToNoContentResult`)
- `src/Servicios/Hoteles/Hoteles.Domain/Hoteles/Hotel.cs` (`Editar`/`Eliminar`/`Eliminado`)
- `src/Servicios/Hoteles/Hoteles.Domain/Puertos/IHotelRepository.cs`
- `src/Servicios/Hoteles/Hoteles.Infrastructure/Persistencia/{HotelRepository,HotelesDbContext}.cs`
- `src/Servicios/Hoteles/Hoteles.Application/Hoteles/CrearHotel/{CrearHotelCommand,CrearHotelCommandHandler,CrearHotelCommandValidator}.cs`
- `src/Servicios/Hoteles/Hoteles.Api/Program.cs` (PUT/DELETE + handler transversal)
- `src/Servicios/Reservas/Reservas.Domain/Reservas/HabitacionNoDisponibleException.cs` (hereda de `ExcepcionNegocio`)
- `src/Servicios/Reservas/Reservas.Api/Program.cs` (usa `AddManejoExcepcionesNegocio`)
- **Borrado:** `src/Servicios/Reservas/Reservas.Api/Http/ManejadorExcepcionesNegocio.cs`

**Tests (nuevos):**
- `tests/Comun.Web.UnitTests/` (proyecto nuevo — batería del handler transversal)
- `tests/Hoteles.IntegrationTests/` (proyecto nuevo — Testcontainers: concurrencia optimista real, soft delete, bordes)
- `tests/Hoteles.UnitTests/{Fakes.cs, EditarHotel/*, EliminarHotel/*}`
- `tests/Reservas.UnitTests/Dominio/HabitacionNoDisponibleExceptionTests.cs`

### Change Log

- 2026-07-09 — Implementada 2.2 (editar + soft delete + concurrencia optimista) con transversal `ExcepcionNegocio`/handler (Alternativa C, party-mode). Code review adversarial + fix del no-op de concurrencia. Refinamientos de contrato (rowVersion en respuesta, Estado fuera del PUT) vía party-mode. 109 tests verdes. Status → done.

## Review Findings (bmad-code-review · 2026-07-09)

Revisión formal 3 capas. Los 4 AC ✅ + decisiones de party-mode verificadas. Sin bugs de corrección (concurrencia bien arbitrada: override del token del cliente + exclusión de `Seq` + no-op cubierto).

- [x] [Review][Defer] `ExcepcionNegocio.Message` se refleja al cliente — hoy seguro (mensajes curados); riesgo latente si un subtipo futuro envuelve el `Message` de una excepción interna. Diferido (guardia de fuga).
- Dismiss: DELETE con body y ETag/If-Match (ya en `deferred-work`); rowVersion de otra fila → 409 (aceptable); body null → NRE (minimal API responde 400 antes); `Id` redundante en el comando (ruta manda, documentado); comentario 1205 (contraste taxonómico correcto, Hoteles no configura retry).
