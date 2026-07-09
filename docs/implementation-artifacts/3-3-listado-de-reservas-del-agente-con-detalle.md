# Story 3.3: Listado de reservas del agente con detalle

Status: ready-for-dev

<!-- Generado por bmad-create-story. Complejidad NORMAL → tests convencionales + TDD (Red→Green visible);
NO BDD/Gherkin ceremonial (decisión party-mode: BDD solo en 3.1 y E4). Tiene una decisión importante de
frontera BC (aislamiento por agente) → Task 0 party-mode. -->

## Story

Como **agente**,
quiero **listar las reservas de mis hoteles y ver su detalle**,
para **conciliar comisiones**.

## Acceptance Criteria

1. **AC-E3.3.1 — Contenido del listado y detalle.** Dado reservas en los hoteles del agente, cuando consulto el
   listado, entonces cada ítem muestra **hotel, habitación, estancia, estado y precio**; el **detalle** añade
   **huéspedes y contacto de emergencia**.
2. **AC-E3.3.2 — Aislamiento (AC negativo).** Dado reservas de hoteles de **otro** agente, cuando consulto mi
   listado, entonces esas reservas **no** aparecen (aislamiento resuelto **server-side**, no confiando en el cliente).

## Tasks / Subtasks

> **Task 0 (party-mode, PRIMERO) — decisión de frontera BC: ¿cómo sabe Reservas qué hoteles son del agente?**
> El aislamiento (AC-E3.3.2) exige mapear reserva → habitación → hotel → **agente-dueño**. Hoteles es dueño del
> catálogo y del owner; Reservas solo tiene su read-model. Opciones:
> - **(a)** Los eventos de catálogo de 2.5 (o un campo del read-model `ProyeccionHabitacion`) incluyen el
>   `agenteId`/owner del hotel → el filtro es un `WHERE agenteId = @actual` local. Requiere que el contrato de
>   eventos lo transporte (¿ampliar `HabitacionAgregada` o un evento `HotelRegistradoPorAgente`?).
> - **(b)** El `agenteId` sale de un claim de auth (identidad del agente) y el read-model ya asocia hotel→agente
>   por otra vía. En el alcance actual NO hay auth real (F2/seguridad diferida) → habría que simular la identidad.
> - **(c)** Denormalizar el `agenteId` en la reserva al crearla (el `CrearReservaCommand` ya recibía `AgenteEmail`).
>   Esto evita cruzar la frontera BC pero acopla el agente al momento de la reserva.
> Resolver con `/bmad-party-mode` (Winston + John + Amelia) y documentar antes de implementar. Recomendación
> previa: (c) si el `AgenteEmail`/agenteId ya viaja en el comando de reserva (más simple, sin tocar contrato de
> catálogo), validando que satisface la conciliación de comisiones "de mis hoteles".

- [ ] **Task 1 — Cerrar la deuda de persistencia de huéspedes/contacto (habilita el DETALLE de AC-E3.3.1)** *(TDD)*
  - [ ] `Reserva` persiste huéspedes (owned collection) + contacto de emergencia (owned) — deuda DIFERIDA de 1.6a
    (ver `deferred-work.md`: "Persistir huéspedes/contacto en el agregado → 1.6b"). Mapeo EF owned + migración.
  - [ ] Tests de integración (Testcontainers) del round-trip de persistencia; no romper los tests de creación de reserva de E1.
- [ ] **Task 2 — Query de listado (AC: 1, 2)** *(TDD Red→Green)*
  - [ ] `ListarReservasDelAgenteQuery` + handler en `Reservas.Application/Reservas/ListarReservasDelAgente/` (`IQuery`, no `ICommand`). Devuelve ítems con hotel, habitación, estancia, estado, precio; filtra server-side por el agente (según Task 0).
  - [ ] AC negativo: reservas de otro agente NO aparecen (test explícito con dos agentes).
- [ ] **Task 3 — Detalle de reserva (AC: 1)** *(TDD)*
  - [ ] `ObtenerReservaDetalleQuery` (o expandir el ítem) → añade huéspedes + contacto de emergencia. Aislamiento: el detalle de una reserva de otro agente devuelve 404/403, no el contenido.
- [ ] **Task 4 — Endpoints (AC: 1, 2)**
  - [ ] `GET /api/v1/reservas` (listado del agente) y `GET /api/v1/reservas/{id}` (detalle) en `Reservas.Api`; Result→HTTP. OpenAPI/Scalar. La identidad del agente, según Task 0.
- [ ] **Task 5 — Tests (unit + integración Testcontainers)**
  - [ ] Contenido correcto del listado/detalle; aislamiento con dos agentes; bordes (agente sin reservas → lista vacía).
- [ ] **Task 6 — Commits TDD (Red→Green visibles) en rama `feature/3-3-listado-reservas` + PR a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Deuda que esta historia cierra (persistencia de huéspedes/contacto)

- 1.6a validó huéspedes + contacto de emergencia (FR-10/11) y los llevó al evento `ReservaConfirmada`, pero **NO
  los persistió** en el agregado `Reserva` (era unit-only). El **detalle** de AC-E3.3.1 los exige → 3.3 añade el
  mapeo EF (owned collection `Huespedes` + owned `ContactoEmergencia`) + migración + test de integración.
  [Source: deferred-work.md · code review of story-1.6a]

### Decisión de frontera BC (Task 0)

- El aislamiento por agente cruza la frontera Reservas↔Hoteles (owner del hotel). Ver Task 0. NO acoplar
  síncronamente los BC; si el owner debe viajar, que sea por el contrato de eventos o denormalizado en la reserva.

### Arquitectura (fuente `architecture.md`)

- **CQRS lectura:** query sobre datos de Reservas (reservas + slots) — no toca el invariante de escritura.
  [Source: architecture.md#CQRS]
- **Aislamiento server-side:** el filtro por agente se aplica en el servidor (nunca confiar en un parámetro del
  cliente para la autorización de datos). [Source: AC-E3.3.2]
- **Seguridad/auth diferida (F2):** no hay identidad real de agente todavía; documentar cómo se simula en el
  alcance actual sin hardcodear credenciales. [Source: architecture.md#Seguridad]

### Previous story intelligence (3.2)

- 3.2 introduce las queries de lectura (`IQuery`) sobre el read-model; 3.3 sigue el mismo patrón pero sobre
  datos de reservas. Reutilizar el estilo de query/handler/DTO y el mapeo Result→HTTP.
- La estancia es semiabierta `[entrada, salida)`; el precio se calculó en E1 (`CalculadorPrecio`) — mostrarlo tal
  como se persistió, no recalcular.

### Anti-patrones a evitar

- Filtrar por agente en el cliente (rompe AC-E3.3.2; debe ser server-side).
- Llamada síncrona a Hoteles para resolver el owner (rompe la frontera BC — resolver por evento/denormalización).
- Recalcular el precio en la lectura (mostrar el persistido).
- Devolver el detalle de una reserva ajena (fuga de datos entre agentes).

### Testing

- Unit: filtro por agente, forma del DTO listado/detalle. Integración (Testcontainers): persistencia de
  huéspedes/contacto (round-trip), aislamiento con dos agentes, listado vacío.

### Project Structure Notes

- NUEVO `Reservas.Application/Reservas/{ListarReservasDelAgente,ObtenerReservaDetalle}/`. Mapeo EF de owned types
  en `ReservasDbContext` + migración. Endpoints en `Reservas.Api/Program.cs`.

### References

- [epics.md — Story 3.3 (AC-E3.3.1/2)](../planning-artifacts/epics.md)
- [architecture.md — CQRS lectura, seguridad](../planning-artifacts/architecture.md)
- [deferred-work.md](deferred-work.md) — persistir huéspedes/contacto (deuda de 1.6a)
- [Story 3.2](3-2-busqueda-de-habitaciones-disponibles.md) (patrón de query de lectura)

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List

### Change Log
