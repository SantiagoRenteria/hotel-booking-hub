# Story 3.3: Listado de reservas del agente con detalle

---
baseline_commit: 7d0b39a9819a2e3a2facafca34a5ae239561d09e
---

Status: review

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

> **✅ Task 0 RESUELTA (party-mode Winston/John/Amelia, 2026-07-09) — Opción (c): aislar por `AgenteEmail` de la reserva.**
> Decisión de Santiago: el eje de aislamiento es **"mis reservas"** (las que el agente intermedió), no "hoteles que administro" — coherente con *conciliar comisiones*. Se aísla por `AgenteEmail` **persistido en la `Reserva`** (hoy el `CrearReservaCommand` lo recibe pero se descarta — deuda de 1.6a). NO se reabre el contrato de catálogo (se descarta (a) como gold-plating). La **identidad del agente** se resuelve **server-side** tras una costura `IContextoAgente` (impl. por header `X-Agente` hoy → claim de auth en Épica 6), **fail-closed** (sin identidad → NO devuelve todo: falla/vacío). El header es **deuda explícita de Épica 6**, NO un mecanismo de seguridad. **AC-E3.3.2 se reinterpreta como "reservas que hice".** Divergiría de "mis hoteles" solo si un agente reservara en hotel de otro (caso no soportado en el alcance actual; documentado).
>
> <details><summary>Contexto original de la decisión</summary>
>
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
>
> </details>

- [x] **Task 1 — Cerrar la deuda de persistencia de huéspedes/contacto (habilita el DETALLE de AC-E3.3.1)** *(TDD)*
  - [x] `Reserva` persiste huéspedes (owned collection `ReservaHuespedes` con `Documento` anidado) + contacto (owned) + `AgenteEmail` + `PrecioTotal`. Mapeo EF owned + migración `PersisteDatosReserva`. (`Huesped` gana ctor sin parámetros + setters privados: EF no bindea el owned anidado por constructor.)
  - [x] Test de integración (Testcontainers) del round-trip; `Reserva.Crear` con parámetros opcionales → los tests de anti-overbooking/outbox de E1 no se rompen.
- [x] **Task 2 — Query de listado (AC: 1, 2)** *(TDD Red→Green)*
  - [x] `ListarReservasDelAgenteQuery` + handler en `Reservas.Application/Reservas/ListarReservasDelAgente/` (`IRequest`, no `ICommand`). Ítems con hotel, habitación, estancia, estado, precio (LEFT JOIN con `ProyeccionHabitacion`); filtra server-side por `AgenteEmail` (Opción c). La identidad viene de `IContextoAgente`, NO del cliente.
  - [x] AC negativo: reservas de otro agente NO aparecen (test con dos agentes).
- [x] **Task 3 — Detalle de reserva (AC: 1)** *(TDD)*
  - [x] `ObtenerReservaDetalleQuery` → añade huéspedes + contacto. Aislamiento: el lector filtra por agente → reserva ajena/inexistente devuelve null → **404** (no filtra contenido).
- [x] **Task 4 — Endpoints (AC: 1, 2)**
  - [x] `GET /api/v1/reservas` (listado) y `GET /api/v1/reservas/{id}` (detalle) en `Reservas.Api`; Result→HTTP (`ToOkResult`). Identidad del agente vía `HttpContextoAgente` (cabecera `X-Agente`), fail-closed (sin identidad → 403).
- [x] **Task 5 — Tests (unit + integración Testcontainers)**
  - [x] Unit: fail-closed (403) + 404 de ajena/inexistente + filtrado por agente. Integración: round-trip, contenido del listado/detalle, aislamiento con dos agentes, agente sin reservas → vacío.
- [x] **Task 6 — Commits en rama `feature/3-3-listado-reservas` + PR a `develop`** (autor Santiago Renteria; sin trailers)

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

claude-opus-4-8 (bmad-dev-story, modo autónomo).

### Debug Log References

- Suite completa: 261 tests en verde — Reservas.Unit 80, Reservas.Int 34, Hoteles.Unit 100, Hoteles.Int 19, Contracts 13, Comun.Web 15. `dotnet format` limpio (migración EF reformateada).

### Completion Notes List

- **Task 0 (party-mode):** aislamiento por `AgenteEmail` de la reserva (Opción c). Identidad server-side vía `IContextoAgente` (cabecera `X-Agente`, `HttpContextoAgente`), **fail-closed** (403 sin identidad). Deuda explícita de Épica 6 (no es auth).
- **Task 1:** `Reserva` ahora persiste `Huespedes` (owned collection), `ContactoEmergencia` (owned), `AgenteEmail` y `PrecioTotal` — cierra la deuda de 1.6a. `Reserva.Crear` recibe esos datos como parámetros **opcionales** → los tests de anti-overbooking/outbox de E1 (que solo pasan habitación+estancia) siguen compilando y verdes. `Huesped` requirió ctor sin parámetros + setters privados (EF no bindea el owned `Documento` por constructor).
- **Task 2/3:** queries CQRS (`IRequest`, sin `TransactionBehavior`). `LectorReservasAgenteSql` filtra por `AgenteEmail` DENTRO de la consulta (aislamiento server-side) y cruza con `ProyeccionHabitacion` (LEFT JOIN, la reserva aparece aunque la proyección no esté hidratada). Detalle de ajena/inexistente → null → 404 (no filtra existencia). Precio mostrado tal cual (sin recálculo).
- **Task 4:** endpoints `GET /api/v1/reservas` y `/{id}`. La identidad NO viaja en la query (el cliente no elige a qué agente ve).
- **Deuda conocida:** el listado no muestra `HotelNombre` (no está en `ProyeccionHabitacion` — diferido de 3.1); se expone `HotelId` + ciudad + tipo/ubicación. El eje "mis reservas" (no "mis hoteles") es la interpretación acordada de AC-E3.3.2.

### File List

**Nuevos (producción):**
- `src/Servicios/Reservas/Reservas.Application/Abstracciones/IContextoAgente.cs`
- `src/Servicios/Reservas/Reservas.Application/Abstracciones/ILectorReservasAgente.cs`
- `src/Servicios/Reservas/Reservas.Application/Reservas/ListarReservasDelAgente/ListarReservasDelAgenteQuery.cs`
- `src/Servicios/Reservas/Reservas.Application/Reservas/ListarReservasDelAgente/ListarReservasDelAgenteQueryHandler.cs`
- `src/Servicios/Reservas/Reservas.Application/Reservas/ObtenerReservaDetalle/ObtenerReservaDetalleQuery.cs`
- `src/Servicios/Reservas/Reservas.Application/Reservas/ObtenerReservaDetalle/ObtenerReservaDetalleQueryHandler.cs`
- `src/Servicios/Reservas/Reservas.Infrastructure/Proyeccion/LectorReservasAgenteSql.cs`
- `src/Servicios/Reservas/Reservas.Api/HttpContextoAgente.cs`
- Migración `Reservas.Infrastructure/Migraciones/*_PersisteDatosReserva.cs`

**Modificados (producción):**
- `Reservas.Domain/Reservas/Reserva.cs` (persistir agente/precio/huéspedes/contacto; `Crear` con opcionales)
- `Reservas.Domain/Reservas/Huesped.cs` (ctor sin parámetros + setters privados para EF)
- `Reservas.Application/Reservas/CrearReserva/CrearReservaCommandHandler.cs` (persiste en vez de descartar)
- `Reservas.Infrastructure/Persistencia/ReservasDbContext.cs` (owned mappings)
- `Reservas.Infrastructure/RegistroInfraestructura.cs` (registro del lector)
- `Reservas.Api/Program.cs` (identidad + endpoints)

**Nuevos (tests):**
- `tests/Reservas.IntegrationTests/ListadoReservasAgenteTests.cs`
- `tests/Reservas.UnitTests/Reservas/ListarReservas/ListadoReservasHandlersTests.cs`

### Change Log

- 2026-07-09 — Story 3.3 implementada. Task 0 (party-mode): aislamiento por `AgenteEmail`, identidad server-side vía `IContextoAgente` fail-closed. Persistencia de huéspedes/contacto/agente/precio (cierra deuda 1.6a). Queries CQRS de listado + detalle con aislamiento server-side. Endpoints `GET /api/v1/reservas[/{id}]`. Suite completa 261 tests en verde.
