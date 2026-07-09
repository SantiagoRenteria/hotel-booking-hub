---
baseline_commit: 887e0b1
---

# Story 2.4: Gestionar habitaciones del hotel

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

Como **agente**,
quiero **añadir, editar y habilitar/deshabilitar habitaciones de un hotel**,
para **gestionar el inventario ofertable con precisión**.

> **Depende de 2.1–2.3.** Introduce el aggregate **`Habitacion`** (unidad reservable) en el BC de Hoteles, con las mismas claves ADR-017 (UUID v7 PK no-clustered + `Seq` shadow clustered + `rowversion`), y reutiliza toda la maquinaria ya construida: `IHotelRepository`/patrón repositorio, concurrencia optimista (`GuardarConcurrenciaAsync`), transversal `ExcepcionNegocio`/handler, mapeo `Result→HTTP` (`ToCreatedResult`/`ToOkResult`), `LongitudesHotel`-equivalente para habitación.
> **Límite de alcance honesto:** AC-E2.4.3 "ofertabilidad compuesta" (habitación deshabilitada **o** de hotel deshabilitado → no se oferta) se resuelve en la **búsqueda de E3** (proyección). En 2.4 se persisten el estado de la habitación y su pertenencia al hotel; la composición hotel×habitación en la búsqueda es E3. NO emite eventos de catálogo (2.5).

## Acceptance Criteria

1. **AC-E2.4.1 — Añadir habitación.** Dado un hotel existente, cuando añado una habitación con tipo, costo base, impuestos, ubicación y estado (`CrearHabitacionCommand`), entonces responde `201` con la `Habitacion` creada (y su `rowVersion`). Si el hotel no existe (o está eliminado), `404`/`400` según corresponda.
2. **AC-E2.4.2 — Edición independiente.** Dado una habitación existente, cuando la edito (`EditarHabitacionCommand`, con `rowVersion`), entonces cambian solo sus datos; el `Hotel` no se altera; responde `200`. Concurrencia optimista → `409`.
3. **AC-E2.4.3 — Ofertabilidad (estado local).** Dado una habitación, cuando la habilito/deshabilito (`CambiarEstadoHabitacionCommand`), entonces su `Estado` se persiste y responde `200`. *(La composición "no se oferta si la habitación o su hotel están deshabilitados" se prueba en la búsqueda de E3; aquí se prueba la transición de estado local + concurrencia.)*
4. **AC-E2.4.4 — No encontrado / concurrencia.** Dado un `Id` de habitación inexistente → `404`; dos ediciones/transiciones concurrentes con el mismo `rowVersion` → `exactamente 1` confirma, la otra `409` (nunca `500`).

## Tasks / Subtasks

- [x] **Task 1 — Dominio: aggregate `Habitacion`** — `Habitacion` (aggregate root independiente, claves ADR-017) con factory `Crear` (invariantes: hotelId, tipo/ubicación no vacíos, montos ≥ 0), `Editar` (no toca estado ni hotel), `Habilitar`/`Deshabilitar` (idempotentes); `EstadoHabitacion` enum; `LongitudesHabitacion` (fuente única)
- [x] **Task 2 — Persistencia** — `DbSet<Habitacion>` + mapeo (ADR-017, `rowversion`, `decimal(18,2)`, `Estado` string, índice `HotelId` SIN FK constraint → independencia de aggregates); migración `AgregaHabitaciones`; `IHabitacionRepository` + adaptador (mismo patrón de concurrencia); verificación de hotel existente al crear
- [x] **Task 3 — Slices** — `CrearHabitacion` (201), `EditarHabitacion` (200), `CambiarEstadoHabitacion` (200, unificado + `switch` exhaustivo) + validators; `HabitacionResponseDto` (con `rowVersion`, fábrica `De(...)` compartida) en `Hoteles.Application.Habitaciones`
- [x] **Task 4 — Endpoints** — `POST /hoteles/{hotelId}/habitaciones`, `PUT /habitaciones/{id}`, `POST /habitaciones/{id}/habilitar`|`/deshabilitar` (estado por ruta)
- [x] **Task 5 — Tests** — Unit: crear (201 + 404 hotel inexistente), editar (200/404/409), habilitar/deshabilitar (200 + idempotencia), validators, mapeo Result→HTTP. Integración (Testcontainers): persistir, editar no altera el hotel, deshabilitar persiste, concurrencia real (1/1 `409`)
- [x] **Task 6 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Aggregate `Habitacion` independiente del `Hotel`

- `Habitacion` es su propio aggregate root (no un owned type de `Hotel`): se gestiona y versiona de forma independiente (AC-E2.4.2). Referencia al hotel por `HotelId` (no navegación bidireccional obligatoria). Editar una habitación NO carga ni modifica el `Hotel`.
- Al **crear** una habitación se valida que el hotel exista y esté activo (reutilizar `IHotelRepository.ObtenerAsync` o una comprobación de existencia). Decidir en dev si "hotel inexistente" es 404 (recurso padre) o 400 (validación de FK) — preferible **404** (la ruta anida bajo `/hoteles/{hotelId}`).

### Reutilización (2.1–2.3)

- Claves ADR-017, `rowversion`, `GuardarConcurrenciaAsync` (fuerza UPDATE excluyendo `Seq`, traduce `DbUpdateConcurrencyException`→409), transversal `ExcepcionNegocio`/handler, `ToCreatedResult`/`ToOkResult`, patrón de slice unificado para estado (como `CambiarEstadoHotel`). `HabitacionResponseDto` expone `rowVersion` (contrato de 2.2).

### Dinero (costo base + impuestos)

- Son atributos de **catálogo** de la habitación (decimal con precisión fija). El cálculo del precio de una reserva vive en Reservas (`CalculadorPrecio`); aquí solo se almacenan. Validar ≥ 0.

### Límite de alcance

- NO eventos de catálogo (2.5) ni búsqueda/proyección (E3). La ofertabilidad compuesta (AC-E2.4.3) se prueba en E3. NO borrado de habitaciones (el enunciado pide añadir/editar/estado; el soft-delete de habitación no está en los AC — no implementarlo salvo que se decida).

### Anti-patrones a evitar

- Modelar `Habitacion` como owned del `Hotel` (rompería la gestión/versionado independiente).
- Cargar/mutar el `Hotel` al editar una habitación.
- Duplicar el patrón de concurrencia/estado en vez de reutilizar el de Hotel.
- Mapear concurrencia a 500 (debe ser 409, ya cubierto por el transversal).

### Testing

- Unit en `Hoteles.UnitTests` (nuevo folder `Habitaciones`/por slice). Integración en `Hoteles.IntegrationTests` (Testcontainers, patrón de 2.2/2.3): la concurrencia y la independencia hotel-habitación se prueban contra SQL real.

### Previous story intelligence (2.1–2.3)

- Todo el andamiaje transversal y de persistencia con concurrencia está probado (121 tests). 2.4 replica el patrón `Hotel` para `Habitacion` + la relación FK. Ver `deferred-work.md`: tests de wiring HTTP end-to-end siguen diferidos (abordar con `Hoteles.FunctionalTests` cuando se decida).

### Git

- Commit + push a `develop`; autor **Santiago Renteria**, sin coautoría IA.

### Project Structure Notes

- `Hoteles.Domain/Habitaciones/{Habitacion,EstadoHabitacion,LongitudesHabitacion}.cs` + `Puertos/IHabitacionRepository.cs`; `Hoteles.Application/Habitaciones/{CrearHabitacion,EditarHabitacion,CambiarEstadoHabitacion}/`; `Hoteles.Infrastructure/Persistencia/` (mapeo + repo + migración); `Hoteles.Api/Program.cs` (endpoints). Tests en `tests/Hoteles.UnitTests/` + `tests/Hoteles.IntegrationTests/`.

### References

- [epics.md — Story 2.4](../planning-artifacts/epics.md) (AC-E2.4.1..3, FR-5/6/7).
- [architecture.md — ADR-017 (claves) / Result→HTTP / concurrencia](../planning-artifacts/architecture.md).
- Stories 2.1 (base Hoteles), 2.2 (concurrencia + transversal), 2.3 (transición de estado unificada).
- [deferred-work.md](deferred-work.md).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (modo autónomo).

### Debug Log References

- Sin incidencias de motor. Se aplicaron proactivamente las lecciones de 2.2/2.3 (concurrencia `State=Modified` excluyendo `Seq`; `switch` exhaustivo en la transición de estado).

### Completion Notes List

- **`Habitacion` como aggregate independiente** (no owned type): referencia al hotel por `HotelId` + índice, SIN FK constraint, para no acoplar los aggregates (gestión/versionado separados, AC-E2.4.2). Probado por integración (`Editar_habitacion_no_altera_el_hotel`).
- **Reutilización total** de 2.1–2.3: claves ADR-017, `GuardarConcurrenciaAsync`, transversal `ExcepcionNegocio`/handler, `ToCreatedResult`/`ToOkResult`, patrón de estado unificado (`CambiarEstadoHabitacion`, como `CambiarEstadoHotel`). `LongitudesHabitacion` fuente única (validators + EF); `HabitacionResponseDto.De(...)` fábrica única del mapeo.
- **Code review adversarial (1 capa):** (1) wording "hotel activo"→"hotel con id" (el gate es «no eliminado»; un hotel deshabilitado SÍ admite habitaciones — coherente con AC-E2.4.3); (2) añadido happy-path de *habilitar* (unit) + persistencia de estado (integración); (3) documentado el trade-off de habitación huérfana por race hotel-eliminado (check-then-act sin FK; E3 filtra) en `deferred-work.md`. Camino feliz, AC y convenciones correctos.
- **Alcance honesto:** AC-E2.4.3 "ofertabilidad compuesta" (no se oferta si habitación o su hotel están deshabilitados) se resuelve en la búsqueda de E3; 2.4 prueba la transición de estado local + persistencia + concurrencia. Sin eventos de catálogo (2.5).
- **Gates finales:** build 0/0, `dotnet format` limpio. Tests: Hoteles unit 61/61, Hoteles integración 12/12, Comun.Web 11/11, Reservas unit 51/51, Reservas integración 11/11 (**146 total**; Reservas/Comun sin cambios).

### File List

**Nuevos — dominio/persistencia:**
- `src/Servicios/Hoteles/Hoteles.Domain/Habitaciones/{Habitacion,EstadoHabitacion,LongitudesHabitacion}.cs`
- `src/Servicios/Hoteles/Hoteles.Domain/Puertos/IHabitacionRepository.cs`
- `src/Servicios/Hoteles/Hoteles.Infrastructure/Persistencia/HabitacionRepository.cs`
- `src/Servicios/Hoteles/Hoteles.Infrastructure/Migraciones/20260709113506_AgregaHabitaciones*.cs`

**Nuevos — aplicación:**
- `src/Servicios/Hoteles/Hoteles.Application/Habitaciones/HabitacionResponseDto.cs`
- `.../Habitaciones/{CrearHabitacion,EditarHabitacion,CambiarEstadoHabitacion}/` (command + handler + validator cada uno)

**Modificados:**
- `src/Servicios/Hoteles/Hoteles.Infrastructure/Persistencia/HotelesDbContext.cs` (`DbSet<Habitacion>` + mapeo)
- `src/Servicios/Hoteles/Hoteles.Infrastructure/RegistroInfraestructura.cs` (`IHabitacionRepository`)
- `src/Servicios/Hoteles/Hoteles.Api/Program.cs` (endpoints de habitaciones)

**Tests (nuevos):**
- `tests/Hoteles.UnitTests/FakesHabitacion.cs`, `tests/Hoteles.UnitTests/Habitaciones/*`
- `tests/Hoteles.IntegrationTests/HabitacionTests.cs`

### Change Log

- 2026-07-09 — Implementada 2.4 (gestionar habitaciones): aggregate `Habitacion` independiente + 3 slices (crear/editar/estado) reutilizando concurrencia + transversal de 2.1–2.3. Code review adversarial + fixes (wording, cobertura de habilitar, trade-off documentado). 146 tests verdes. Status → done.
