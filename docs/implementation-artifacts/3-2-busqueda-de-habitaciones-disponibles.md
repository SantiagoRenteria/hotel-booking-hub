# Story 3.2: Búsqueda de habitaciones disponibles

Status: ready-for-dev

<!-- Generado por bmad-create-story. Complejidad NORMAL (query de lectura CQRS sobre la ProyeccionHabitacion
de 3.1). Tests convencionales + TDD (Red→Green visible); NO BDD/Gherkin ceremonial (decisión party-mode:
BDD reservado para 3.1 y E4). -->

## Story

Como **viajero**,
quiero **buscar habitaciones por ciudad, fechas y número de huéspedes**,
para **encontrar solo opciones realmente disponibles**.

Es la puerta de entrada de lectura (CQRS): sirve desde la `ProyeccionHabitacion` (read-model de 3.1, que
combina catálogo + disponibilidad), nunca desde el catálogo de Hoteles directo. El invariante duro de
overbooking sigue en el motor de Reservas; esta búsqueda es best-effort (consistencia eventual) y a lo sumo
produce 409 evitables, nunca overbooking.

## Acceptance Criteria

1. **AC-E3.2.1 — Filtro de disponibilidad real.** Dado habitaciones en una ciudad, cuando busco por ciudad,
   `[entrada, salida)` y huéspedes, entonces el resultado incluye **solo** habitaciones activas, con
   `capacidad >= huéspedes` y con **todas** las noches del rango libres.
2. **AC-E3.2.2 — No mostrar lo no disponible (AC negativo).** Una habitación ya reservada en `[entrada, salida)`,
   o deshabilitada, o de hotel deshabilitado, **no** aparece en los resultados.
3. **AC-E3.2.3 — Caché de lectura.** Una búsqueda repetida se sirve desde caché Redis vigente; una invalidación
   disparada por un evento de catálogo (o por cambio de disponibilidad) refresca el resultado.

## Tasks / Subtasks

- [ ] **Task 1 — Query de disponibilidad (AC: 1, 2)** *(TDD Red→Green)*
  - [ ] `BuscarDisponibilidadQuery` + handler en `Reservas.Application/Reservas/BuscarDisponibilidad/` (patrón `IQuery`/`IRequestHandler`, NO `ICommand` → sin `TransactionBehavior`).
  - [ ] Lee la `ProyeccionHabitacion` (3.1): filtra por ciudad, `activa == true`, `capacidad >= huéspedes`, y todas las noches de `[entrada, salida)` libres (sin solapamiento con noches ocupadas del read-model).
  - [ ] Semiabierto `[entrada, salida)`: la noche de salida NO se cuenta (consistente con `Estancia`/`NochesHabitacion` de E1). Validación de entrada (fechas, huéspedes >= 1) en el validator.
- [ ] **Task 2 — AC negativo explícito (AC: 2)** *(tests de tabla)*
  - [ ] Casos: reservada-en-rango, deshabilitada, hotel-deshabilitado, capacidad-insuficiente, solapamiento-parcial de fechas → NO aparece. Bordes de rango (salida == entrada de otra reserva → SÍ disponible por semiabierto).
- [ ] **Task 3 — Caché de lectura Redis (AC: 3)** *(según decisión de invalidación)*
  - [ ] Cachear el resultado por clave normalizada `(ciudad, entrada, salida, huéspedes)` con TTL; invalidar/expirar ante evento de catálogo o cambio de disponibilidad que afecte la ciudad. Confirmar estrategia de invalidación (TTL corto vs invalidación dirigida) — si es decisión no trivial, `/bmad-party-mode`.
- [ ] **Task 4 — Endpoint (AC: 1, 2, 3)**
  - [ ] `GET /api/v1/habitaciones/disponibles?ciudad=&entrada=&salida=&huespedes=` en `Reservas.Api` (CAP-4/FR-8); Result→HTTP (`ToOkResult`). OpenAPI/Scalar.
- [ ] **Task 5 — Tests de integración (Testcontainers SQL + Redis)**
  - [ ] Sembrar proyección + slots; verificar filtro real, AC negativos y caché (hit/refresh tras invalidación).
- [ ] **Task 6 — Commits TDD (Red→Green visibles) en rama `feature/3-2-busqueda` + PR a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Dependencia dura: Story 3.1

- 3.2 **consume** la `ProyeccionHabitacion` de 3.1 (read-model idempotente/ordenado que combina catálogo +
  disponibilidad). 3.1 está `ready-for-dev` (aún no implementada). **No implementar 3.2 antes que 3.1** exista
  en `develop`, o la búsqueda no tendría de dónde leer. El esquema exacto del read-model (tabla SQL vs Redis)
  lo fija el `Task 0 D2` de 3.1 — 3.2 debe alinearse con esa decisión. [Source: 3-1 story · Task 0 D2]

### Arquitectura (fuente `architecture.md`)

- **Lectura vía proyección + Redis** (NFR-1). La búsqueda se filtra best-effort por la `ProyeccionHabitacion`;
  el invariante duro sigue en el motor (staleness → a lo sumo 409 evitables, nunca overbooking). [Source: architecture.md#Lectura, #Staleness]
- **Estructura:** `Reservas.Application/Reservas/BuscarDisponibilidad` + `Reservas.Infrastructure/Proyeccion` + Redis. [Source: architecture.md#411]
- **Caché Redis** (ADR-012/013): disponibilidad + caché de lectura. Invalidación por evento de catálogo. [Source: architecture.md#203, #456]
- **Semiabierto `[entrada, salida)`:** `Estancia`/`NochesHabitacion` de E1 modelan la estancia como noches; la noche de salida no se ocupa. La búsqueda debe usar el mismo criterio para no falsear solapamientos. [Source: Reservas.Domain/Reservas/Estancia.cs]

### Previous story intelligence (3.1)

- El read-model expone atributos de catálogo (hotel, ciudad, tipo, costo, impuestos, capacidad, activa) y
  disponibilidad (noches ocupadas). La `activa` refleja habilitada/deshabilitada y hotel-deshabilitado (la
  proyección ya aplica `HabitacionDeshabilitada`). **Ojo asimetría 2.5:** una habitación rehabilitada podría
  seguir marcada inactiva si el re-alta no llega por eventos (deuda de contrato registrada). [Source: 3-1 story, deferred-work.md]
- No castear `data` de eventos; el read-model ya está materializado por 3.1 — 3.2 solo lo consulta.

### Anti-patrones a evitar

- Leer del catálogo de Hoteles directamente (rompe la frontera BC; se lee la proyección local).
- Contar la noche de salida como ocupada (falsos negativos en los bordes de rango).
- Caché sin invalidación (mostraría inventario obsoleto indefinidamente).
- Mostrar habitaciones inactivas o de hoteles deshabilitados (rompe AC-E3.2.2).

### Testing

- Unit: lógica de filtro (capacidad, solapamiento de fechas semiabierto, activa). Integración: SQL + Redis
  reales (Testcontainers) — filtro real, AC negativos, caché hit + refresh tras invalidación.

### Project Structure Notes

- NUEVO `Reservas.Application/Reservas/BuscarDisponibilidad/` (query + handler + validator + DTO). Caché en
  `Reservas.Infrastructure` (Redis). Endpoint en `Reservas.Api/Program.cs`. Reutiliza la `ProyeccionHabitacion` de 3.1.

### References

- [epics.md — Story 3.2 (AC-E3.2.1/2/3)](../planning-artifacts/epics.md)
- [architecture.md — Lectura/CQRS, Redis, staleness](../planning-artifacts/architecture.md)
- [Story 3.1](3-1-proyeccion-de-habitaciones-idempotente-y-ordenada.md) (read-model que alimenta esta búsqueda)

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List

### Change Log
