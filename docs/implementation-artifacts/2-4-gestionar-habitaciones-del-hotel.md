# Story 2.4: Gestionar habitaciones del hotel

Status: ready-for-dev

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

- [ ] **Task 1 — Dominio: aggregate `Habitacion` (AC: 1, 2, 3)**
  - [ ] `Habitacion` (aggregate root): `Id` (UUID v7), `HotelId`, `Tipo`, `CostoBase` (decimal), `Impuestos` (decimal), `Ubicacion`, `Estado` (`EstadoHabitacion` {Habilitada, Deshabilitada}). Factory `Crear(...)` con invariantes (hotelId no vacío, costoBase/impuestos ≥ 0, tipo/ubicación no vacíos). Métodos `Editar(...)` (NO cambia estado ni hotel), `Habilitar()`/`Deshabilitar()` (idempotentes) — mismo patrón que `Hotel`
  - [ ] `EstadoHabitacion` enum (string en BD). `LongitudesHabitacion` (fuente única de topes de texto)
- [ ] **Task 2 — Persistencia (AC: 1, 2, 3, 4)**
  - [ ] `HotelesDbContext`: `DbSet<Habitacion>` + mapeo (claves ADR-017, `rowversion`, `HasMaxLength`, `Estado` `HasConversion<string>`, `CostoBase`/`Impuestos` con precisión `decimal(18,2)` o la del proyecto, FK `HotelId`); relación con `Hotel` sin romper su independencia. Migración (`AgregaHabitaciones`)
  - [ ] `IHabitacionRepository` (`CrearAsync`→rowVersion, `ObtenerAsync`, `GuardarConcurrenciaAsync`) + adaptador. Verificación de existencia del hotel al crear (hotel activo)
- [ ] **Task 3 — Slices (AC: 1, 2, 3, 4)**
  - [ ] `CrearHabitacion` (201), `EditarHabitacion` (200, con `rowVersion`), `CambiarEstadoHabitacion` (200, unificado como en 2.3) + validators (`MaximumLength`, costo/impuestos ≥ 0, rowVersion presente)
  - [ ] `HabitacionResponseDto` (incluye `rowVersion` base64), en `Hoteles.Application.Hoteles` (o subcarpeta `Habitaciones`)
- [ ] **Task 4 — Endpoints (AC: 1, 2, 3)**
  - [ ] `POST /api/v1/hoteles/{hotelId:guid}/habitaciones` (crear), `PUT /api/v1/habitaciones/{id:guid}` (editar), `POST /api/v1/habitaciones/{id:guid}/habilitar`|`/deshabilitar`. (El handler transversal ya está cableado.)
- [ ] **Task 5 — Tests (AC: 1, 2, 3, 4)**
  - [ ] Unit: crear happy (201), editar (200), habilitar/deshabilitar (200 + idempotencia), 404, mapeo Result→HTTP; validators
  - [ ] **Integración (Testcontainers):** crear+persistir; editar no altera el hotel; concurrencia optimista real (1 / 1 `409`); crear en hotel inexistente → 404/400
- [ ] **Task 6 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

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

### Debug Log References

### Completion Notes List

### File List

### Change Log
