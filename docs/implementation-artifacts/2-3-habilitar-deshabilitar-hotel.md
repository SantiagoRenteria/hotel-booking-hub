---
baseline_commit: 63756d1
---

# Story 2.3: Habilitar / deshabilitar hotel

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

Como **agente**,
quiero **habilitar o deshabilitar un hotel** como operación dedicada,
para **controlar al instante si se oferta**.

> **Depende de 2.1/2.2.** Reutiliza el aggregate `Hotel` (con `EstadoHotel` y `rowversion`), el repositorio con concurrencia optimista (`GuardarConcurrenciaAsync`), el transversal `ExcepcionNegocio`/handler y el mapeo `Result→HTTP` (`ToOkResult`). Aterriza la **decisión de party-mode de 2.2**: la transición del ciclo de vida es una operación dedicada con reglas propias, NO un set del PUT de editar.
> **Límite de alcance honesto:** la propagación cross-BC a la proyección de búsqueda de E3 (AC-E2.3.1 "no aparece en búsquedas") se realiza cuando **2.5** emita el evento de catálogo por el outbox. En 2.3 el cambio de estado es **inmediato y local** (el hotel deshabilitado queda `Deshabilitado` y no se ofertará); NO hay búsqueda ni evento todavía (E3/2.5).

## Acceptance Criteria

1. **AC-E2.3.1 — Transición de estado inmediata.** Dado un hotel habilitado, cuando lo deshabilito (`DeshabilitarHotelCommand`), entonces queda `Deshabilitado` y responde `200` con el estado actualizado y el nuevo `rowVersion`; y viceversa al habilitarlo.
2. **AC-E2.3.2 — Idempotencia de la transición.** Dado un hotel ya en el estado objetivo, cuando aplico la misma operación, entonces responde `200` sin error (operación idempotente; el estado final es el pedido).
3. **AC-E2.3.3 — Concurrencia optimista.** Dado dos operaciones concurrentes con el mismo `rowVersion`, cuando ambas guardan, entonces `exactamente 1` confirma y la otra recibe `409` (nunca `500`), como en 2.2.
4. **AC-E2.3.4 — No encontrado / ya eliminado.** Dado un `Id` inexistente o un hotel eliminado lógicamente, cuando habilito/deshabilito, entonces responde `404` (Problem Details), sin efecto.

## Tasks / Subtasks

- [x] **Task 1 — Dominio: transiciones de estado (AC: 1, 2)**
  - [x] `Hotel.Habilitar()` y `Hotel.Deshabilitar()` (fijan `Estado`, idempotentes); documentados como ÚNICA vía de transición (la edición de 2.2 no toca `Estado`)
- [x] **Task 2 — Slice `CambiarEstadoHotel` (AC: 1, 2, 3, 4)**
  - [x] **Unificado (DRY):** un `CambiarEstadoHotelCommand(Id, RowVersion, EstadoObjetivo)` + handler (dispatch `switch` exhaustivo a Habilitar/Deshabilitar) + validator, en vez de dos comandos con flujo duplicado. Las operaciones dedicadas viven en los endpoints (la ruta fija `EstadoObjetivo`)
  - [x] Handler: obtiene (404 si null/eliminado), transición, `GuardarConcurrenciaAsync` (409 en conflicto); validator (id + rowVersion + estado válido)
- [x] **Task 3 — Endpoints + wiring (AC: 1, 3, 4)**
  - [x] `POST /api/v1/hoteles/{id:guid}/habilitar` y `.../deshabilitar`, `comando with { Id = id, EstadoObjetivo = ... }` (ruta autoritativa), `ToOkResult()`
- [x] **Task 4 — Tests (AC: 1, 2, 3, 4)**
  - [x] Unit: deshabilitar/habilitar happy (200 + estado + rowVersion), idempotencia, 404, 409, validator
  - [x] **Integración (Testcontainers):** deshabilitar persiste `Estado=Deshabilitado`; concurrencia real (1 / 1 `409`); estado sobre hotel eliminado → no encontrado (query filter)
- [x] **Task 5 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Transición dedicada vs edición (decisión party-mode de 2.2)

- La habilitación/deshabilitación NO es "editar un atributo": es una transición del ciclo de vida con su propia semántica. Por eso vive en operaciones dedicadas (`:habilitar`/`:deshabilitar`), NO en el PUT de editar (que ya excluye `Estado`). Un solo dueño de la regla de estado.
- Reglas por ahora: transición idempotente y sin precondiciones de reservas (no hay reservas ligadas a hoteles en el alcance actual; el enunciado no exige compensar). Si en el futuro deshabilitar debe bloquear/compensar reservas activas, se añade aquí.

### Concurrencia y persistencia (reutiliza 2.2)

- Los comandos transportan `byte[] RowVersion` (base64 en JSON), igual que editar/eliminar. El handler usa `IHotelRepository.ObtenerAsync` (404 si null/eliminado por el query filter) + `GuardarConcurrenciaAsync` (que fuerza el UPDATE y arbitra el token; conflicto → `ConflictoConcurrenciaException` → 409). La respuesta incluye el nuevo `rowVersion` (contrato de 2.2).

### Límite de alcance

- NO emite eventos de catálogo (eso es 2.5) ni afecta búsquedas (E3, aún no existen). El "reflejo inmediato en ofertabilidad" de AC-E2.3.1 se completa cross-BC en 2.5/E3; en 2.3 se prueba la transición de estado local + concurrencia + 404. NO toca habitaciones (2.4).

### Anti-patrones a evitar

- Duplicar la lógica de obtención+guardado en los dos handlers sin factorizar (pero sin crear una abstracción prematura).
- Reintroducir el cambio de estado en el PUT de editar (rompería el "único dueño").
- Mapear la concurrencia a 500 (debe ser 409, ya cubierto por el transversal).

### Testing

- Unit en `Hoteles.UnitTests` (reutiliza `HotelRepositoryFake`). Integración en `Hoteles.IntegrationTests` (Testcontainers, patrón de 2.2).

### Previous story intelligence (2.2)

- Ya existen: `ExcepcionNegocio`/`ConflictoConcurrenciaException`, handler transversal, `ToOkResult`/`ToNoContentResult`, `GuardarConcurrenciaAsync` (con `State=Modified` excluyendo la shadow `Seq`), `HotelResponseDto` con `RowVersion`. 2.3 es un slice fino sobre esa base.

### Git

- Commit + push a `develop`; autor **Santiago Renteria**, sin coautoría IA.

### Project Structure Notes

- `Hoteles.Domain/Hoteles/Hotel.cs` (`Habilitar`/`Deshabilitar`), `Hoteles.Application/Hoteles/{HabilitarHotel,DeshabilitarHotel}/`, `Hoteles.Api/Program.cs` (endpoints). Tests en `tests/Hoteles.UnitTests/` + `tests/Hoteles.IntegrationTests/`.

### References

- [epics.md — Story 2.3](../planning-artifacts/epics.md) (AC-E2.3.1, FR-4).
- Stories 2.2 (concurrencia + transversal), 2.1 (base Hoteles).
- [deferred-work.md](deferred-work.md) — transición de estado exclusiva de 2.3 (diferido de 2.2).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (modo autónomo).

### Debug Log References

- Sin incidencias. Slice fino sobre la maquinaria ya probada de 2.2 (concurrencia + transversal + mapeo).

### Completion Notes List

- **DRY:** unificado en un solo `CambiarEstadoHotelCommand` con `EstadoObjetivo` (en vez de dos comandos con flujo duplicado); las operaciones dedicadas viven en los dos endpoints, que fijan el estado por ruta (`with { EstadoObjetivo = ... }`) — el cliente no puede fijar un estado arbitrario (solo aporta `rowVersion`). Un solo dueño de la transición; la edición (2.2) no toca el estado.
- **Code review adversarial (1 capa, delta pequeño sobre infra ya revisada):** (1) el dispatch `if/else` degradaría en silencio un futuro tercer estado → cambiado a `switch` exhaustivo que lanza en el `default` (falla ruidosamente); (2) brecha de tests de wiring HTTP end-to-end de los endpoints → transversal a toda la API (ningún slice los tiene; se quitó `WebApplicationFactory` a propósito) → registrado en `deferred-work.md` para un `Hoteles.FunctionalTests` de una vez. Camino feliz, AC, concurrencia y convenciones: correctos.
- **Idempotencia (AC-E2.3.2):** aplicar el estado ya vigente devuelve 200; como `GuardarConcurrenciaAsync` fuerza el UPDATE (heredado de 2.2), el rowVersion se incrementa igual (idempotencia de estado de negocio, no de replay de request — correcto para el contrato de concurrencia).
- **Alcance honesto:** la propagación cross-BC a búsquedas de E3 (AC-E2.3.1) se completa en 2.5/E3; 2.3 prueba la transición local + concurrencia + 404.
- **Gates finales:** build 0/0, `dotnet format` limpio. Tests: Hoteles unit 40/40, Hoteles integración 8/8, Comun.Web 11/11, Reservas unit 51/51, Reservas integración 11/11 (**121 total**; Reservas/Comun sin cambios en 2.3).

### File List

**Nuevos:**
- `src/Servicios/Hoteles/Hoteles.Application/Hoteles/CambiarEstadoHotel/{CambiarEstadoHotelCommand,CambiarEstadoHotelCommandHandler,CambiarEstadoHotelCommandValidator}.cs`
- `tests/Hoteles.UnitTests/CambiarEstadoHotel/{CambiarEstadoHotelCommandHandlerTests,CambiarEstadoHotelCommandValidatorTests}.cs`
- `tests/Hoteles.IntegrationTests/CambioEstadoHotelTests.cs`

**Modificados:**
- `src/Servicios/Hoteles/Hoteles.Domain/Hoteles/Hotel.cs` (`Habilitar`/`Deshabilitar`)
- `src/Servicios/Hoteles/Hoteles.Api/Program.cs` (endpoints `:habilitar`/`:deshabilitar`)

### Change Log

- 2026-07-09 — Implementada 2.3 (habilitar/deshabilitar hotel) como operación dedicada de ciclo de vida, unificada en `CambiarEstadoHotel` (DRY) reutilizando concurrencia + transversal de 2.2. Code review adversarial + fix del dispatch (`switch` exhaustivo). 121 tests verdes. Status → done.
