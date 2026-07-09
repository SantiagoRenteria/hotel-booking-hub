# Story 2.3: Habilitar / deshabilitar hotel

Status: ready-for-dev

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

- [ ] **Task 1 — Dominio: transiciones de estado (AC: 1, 2)**
  - [ ] `Hotel.Habilitar()` y `Hotel.Deshabilitar()` (fijan `Estado`); idempotentes (aplicar el mismo estado no es error). Documentar que son la ÚNICA vía de transición del ciclo de vida (la edición de 2.2 no toca `Estado`)
- [ ] **Task 2 — Slices `HabilitarHotel` / `DeshabilitarHotel` (AC: 1, 2, 3, 4)**
  - [ ] `HabilitarHotelCommand(Id, RowVersion)` y `DeshabilitarHotelCommand(Id, RowVersion)` → `Result<HotelResponseDto>`; handler: obtiene (404 si null/eliminado), aplica la transición, `GuardarConcurrenciaAsync` (409 en conflicto)
  - [ ] Validators (id + rowVersion presentes) — mismo patrón que 2.2
  - [ ] Evaluar si extraer el tronco común de ambos handlers (evitar duplicación) sin sobre-abstraer
- [ ] **Task 3 — Endpoints + wiring (AC: 1, 3, 4)**
  - [ ] `POST /api/v1/hoteles/{id:guid}/habilitar` y `POST /api/v1/hoteles/{id:guid}/deshabilitar` en `Hoteles.Api`, `comando with { Id = id }`, `ToOkResult()`. (El handler transversal y `UseExceptionHandler` ya están cableados en 2.2.)
- [ ] **Task 4 — Tests (AC: 1, 2, 3, 4)**
  - [ ] Unit: deshabilitar happy (200 + estado `Deshabilitado` + nuevo rowVersion), habilitar happy, idempotencia (deshabilitar un ya deshabilitado → 200), 404 (inexistente/eliminado), 409 (propagación de `ConflictoConcurrenciaException`); mapeo Result→HTTP
  - [ ] **Integración (Testcontainers):** deshabilitar persiste `Estado=Deshabilitado`; concurrencia optimista real (1 confirma / 1 `409`); deshabilitar un hotel eliminado → 404 (query filter)
- [ ] **Task 5 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

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

### Debug Log References

### Completion Notes List

### File List

### Change Log
