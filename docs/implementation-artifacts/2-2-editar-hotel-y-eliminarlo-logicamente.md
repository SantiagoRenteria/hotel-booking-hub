# Story 2.2: Editar hotel y eliminarlo lógicamente (soft delete)

Status: ready-for-dev

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

- [ ] **Task 1 — Dominio: mutación y soft delete (AC: 1, 2)**
  - [ ] `Hotel`: métodos `Editar(nombre, ciudad, direccion, descripcion, estado)` y `Eliminar()` (marca `Eliminado = true`); invariantes en el dominio
  - [ ] Propiedad `Eliminado` (bool) + query filter global `HasQueryFilter(h => !h.Eliminado)` en `HotelesDbContext` + migración (`AgregaEliminadoHotel`)
  - [ ] `IHotelRepository`: `Task<Hotel?> ObtenerAsync(Guid id, CancellationToken)` + `Task GuardarAsync(CancellationToken)` (o el patrón que respete la concurrencia optimista); fijar `rowVersion` original como `OriginalValue` para que EF detecte el conflicto
- [ ] **Task 2 — Slices `EditarHotel` / `EliminarHotel` (AC: 1, 2, 4)**
  - [ ] `EditarHotelCommand(Id, RowVersion, ...)` → `Result<HotelResponseDto>`; handler: obtiene (404 si null), aplica `rowVersion` original, edita, guarda; concurrencia → excepción de negocio → 409
  - [ ] `EliminarHotelCommand(Id, RowVersion)` → `Result` (no genérico); handler: obtiene (404), `Eliminar()`, guarda → 204
  - [ ] Validators (rowVersion presente; campos como en 2.1 con `MaximumLength`)
- [ ] **Task 3 — Transversal: excepción de negocio + mapeo Result→HTTP (AC: 3)**
  - [ ] **`ExcepcionNegocio` base en `Comun`** (lleva el código HTTP; p. ej. 409/404) — decisión party-mode de 2.1; `HabitacionNoDisponibleException` (Reservas) pasa a heredar de ella (refactor de lo ya implementado → **party-mode**)
  - [ ] Exception-handler transversal en `Comun.Web` (`IExceptionHandler`) que mapea `ExcepcionNegocio` → su código; registrado por ambos `*.Api`. Sustituye al `ManejadorExcepcionesNegocio` de `Reservas.Api`
  - [ ] `Result<T>.ToOkResult()` y `Result.ToNoContentResult()` en `Comun.Web` (200/204/400/404/409)
  - [ ] `ConflictoConcurrenciaException : ExcepcionNegocio` (409) lanzada al capturar `DbUpdateConcurrencyException`
- [ ] **Task 4 — Endpoint + wiring (AC: 1, 2, 4)**
  - [ ] `PUT /api/v1/hoteles/{id}` (editar) + `DELETE /api/v1/hoteles/{id}` (soft delete) en `Hoteles.Api`; `Program.cs` registra el exception-handler transversal
- [ ] **Task 5 — Tests (AC: 1, 2, 3, 4)**
  - [ ] Unit: editar happy (200), eliminar (204), 404 (inexistente/ya eliminado); mapeo Result→HTTP (200/204/404/409)
  - [ ] **Integración (Testcontainers):** concurrencia optimista real — dos ediciones con el mismo `rowVersion` → 1 confirma, 1 `409` (nunca 500); soft delete desaparece del query filter
- [ ] **Task 6 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

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

### Debug Log References

### Completion Notes List

### File List
