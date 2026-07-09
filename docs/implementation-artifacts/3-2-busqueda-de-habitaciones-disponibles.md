---
baseline_commit: 94c29d09c929b42178764518bf8756bf2f2bceff
---

# Story 3.2: Búsqueda de habitaciones disponibles

Status: in-progress

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

- [x] **Task 1 — Query de disponibilidad (AC: 1, 2)** *(TDD Red→Green)*
  - [x] `BuscarDisponibilidadQuery` + handler en `Reservas.Application/Reservas/BuscarDisponibilidad/` (patrón `IQuery`/`IRequestHandler`, NO `ICommand` → sin `TransactionBehavior`).
  - [x] Lee la `ProyeccionHabitacion` (3.1): filtra por ciudad, `activa == true`, `capacidad >= huéspedes`, y todas las noches de `[entrada, salida)` libres (sin solapamiento con noches ocupadas del read-model).
  - [x] Semiabierto `[entrada, salida)`: la noche de salida NO se cuenta (consistente con `Estancia`/`NochesHabitacion` de E1). Validación de entrada (fechas, huéspedes >= 1) en el validator.
- [x] **Task 2 — AC negativo explícito (AC: 2)** *(tests de tabla)*
  - [x] Casos: reservada-en-rango, deshabilitada, hotel-deshabilitado (colapsado a estado "Deshabilitada"), capacidad-insuficiente, solapamiento-parcial de fechas → NO aparece. Bordes de rango (salida == entrada de otra reserva → SÍ disponible por semiabierto). También fila no hidratada → NO aparece.
- [x] **Task 3 — Caché de lectura Redis (AC: 3)** *(según decisión de invalidación)*
  - [x] Cachear el resultado por clave normalizada `(ciudad, entrada, salida, huéspedes)` con TTL; invalidar ante evento de catálogo que afecte la ciudad. **Decisión (sin party-mode, resoluble):** claves generacionales por ciudad (token en `disp:tok:{ciudad}`) → invalidación O(1) sin escaneo; TTL corto (30 s) cubre best-effort los cambios de disponibilidad no invalidados de forma dirigida.
- [x] **Task 4 — Endpoint (AC: 1, 2, 3)**
  - [x] `GET /api/v1/habitaciones/disponibles?ciudad=&entrada=&salida=&huespedes=` en `Reservas.Api` (CAP-4/FR-8); Result→HTTP (`ToOkResult`). OpenAPI.
- [x] **Task 5 — Tests de integración (Testcontainers SQL + Redis)**
  - [x] Sembrar proyección + slots; verificar filtro real, AC negativos y caché (hit/refresh tras invalidación).
- [x] **Task 6 — Commits TDD (Red→Green visibles) en rama `feature/3-2-busqueda-disponibilidad` + PR a `develop`** (autor Santiago Renteria; sin trailers)
- [ ] **Task 7 — Exclusión de habitaciones de hotel deshabilitado (AC-E3.2.2/FR-7)** *(decisión party-mode: Opción 1b — dimensión independiente de estado de hotel)*
  - [ ] **Contrato:** nuevos eventos `HotelDeshabilitado.v1` / `HotelHabilitado.v1` en `Comun.Eventos` (payload `AggregateId=HotelId` + order key por `Version`).
  - [ ] **Hoteles (Épica 2, aditivo):** `Hotel` gana `Version` monótona; `CambiarEstadoHotelCommandHandler` emite el evento por outbox (1 evento/comando) al habilitar/deshabilitar.
  - [ ] **Reservas — dimensión independiente:** nuevo read-model `ProyeccionHotelEstado` (keyed por `HotelId`, LWW por `VersionEstadoHotel`); `ProyectorCatalogo` lo consume (inbox por `MessageId` ya sirve). NO cascada a `ProyeccionHabitacion` (preserva field-ownership y el estado individual de la habitación).
  - [ ] **Búsqueda:** `BuscadorDisponibilidadSql` filtra `habitacionActiva AND hotelActivo` (join con `ProyeccionHotelEstado`; `COALESCE(hotelActivo, true)` por defecto).
  - [ ] **Migración + backfill:** tabla nueva; **backfill de hoteles ya deshabilitados** (no hay historia de eventos) como tarea de release.
  - [ ] **Tests (gate Murat):** reescribir el caso "hotel deshabilitado" al flujo real (no sembrar estado a mano); desorden (HotelDeshabilitado antes del alta; viejo por versión; permutación completa), ortogonalidad (deshabilitar hotel → deshabilitar habitación individual → rehabilitar hotel → esa habitación sigue fuera), carrera de alta tardía, doble idempotencia (inbox + versión), e2e con await determinista (sin `Sleep`).
- [ ] **Task 8 — Aplicar los 8 `patch` del code review** (ver Review Findings): normalización caché↔SQL, degradar ante fallo de Redis en lectura, try/catch en invalidación post-commit, token leído una sola vez, test e2e de invalidación vía proyector, guarda de rango degenerado, TTL del token, dispose de `RedisCache` en tests.

### Review Findings

_Code review 2026-07-09 (Blind Hunter + Edge Case Hunter + Acceptance Auditor). 1 decisión · 8 patch · 1 defer · 3 descartados._

- [ ] **[Review][Decision → RESUELTO: Opción 1b, party-mode 2026-07-09] AC-E3.2.2 — habitaciones de hotel deshabilitado NO se excluyen** — Deshabilitar un hotel (`CambiarEstadoHotelCommandHandler`) no emite evento ni cascada; no existe `HotelDeshabilitado.v1`. Sus habitaciones quedan `Habilitada` en la proyección y aparecen en la búsqueda. El comentario de `BuscadorDisponibilidadSql` ("incl. hotel deshabilitado, ya colapsado por 2.5") es falso y el test `[InlineData(4,"Deshabilitada")]` lo enmascara sembrando el estado a mano. Viola FR-7 / AC-E3.2.2. **Decisión (party-mode Winston/Amelia/John/Murat, unánime):** ver Task 7 (dimensión independiente de estado de hotel).
- [ ] **[Review][Patch] Normalización asimétrica caché vs SQL** — la clave de caché normaliza `Trim().ToLowerInvariant()` pero el filtro SQL usa la ciudad cruda; entradas con espacios/caso distinto colapsan a la misma clave con resultados SQL distintos (envenenamiento) y la invalidación no cubre variantes de acento/caso. `[Cache/CacheDisponibilidadRedis.cs, Proyeccion/BuscadorDisponibilidadSql.cs]`
- [ ] **[Review][Patch] Fallo de Redis en lectura tumba la búsqueda (500)** — `BuscadorDisponibilidadCacheado` no captura errores de la caché; una caída de Redis debería degradar a leer del read-model (best-effort). `[Proyeccion/BuscadorDisponibilidadCacheado.cs]`
- [ ] **[Review][Patch] Invalidación tras commit sin try/catch** — un fallo de Redis tras `CommitAsync` propaga la excepción con la proyección ya confirmada; en la reentrega el dedup del inbox salta el evento y la invalidación se pierde para siempre. `[Proyeccion/ProyectorCatalogo.cs]`
- [ ] **[Review][Patch] Carrera del token generacional** — `Obtener` y `Guardar` releen el token por separado; una invalidación entre la lectura SQL y el guardado cachea dato viejo bajo el token nuevo. Leer el token una sola vez por búsqueda. `[Cache/CacheDisponibilidadRedis.cs]`
- [ ] **[Review][Patch] Falta test e2e de invalidación por evento de catálogo** — ningún test recorre `ProyectorCatalogo.ProcesarAsync` → `InvalidarCiudadAsync`; el literal de AC-E3.2.3 ("invalidación disparada por un evento de catálogo") queda sin cobertura. `[tests/Reservas.IntegrationTests/CacheDisponibilidadTests.cs]`
- [ ] **[Review][Patch] Buscador sin defensa ante rango degenerado / huéspedes<=0** — vía puerto directo (sin pipeline), `salida<=entrada` o `huespedes<=0` reportan todo el inventario. Guarda defensiva que devuelve vacío. `[Proyeccion/BuscadorDisponibilidadSql.cs]`
- [ ] **[Review][Patch] Token de ciudad sin TTL** — `disp:tok:{ciudad}` nunca expira; bajo `maxmemory-lru` podría evacuarse y resetear la generación. Añadir TTL largo. `[Cache/CacheDisponibilidadRedis.cs]`
- [ ] **[Review][Patch] RedisCache no se dispone en tests** — `RedisFixture.CrearCache()` crea `RedisCache` (IDisposable) sin liberarlo. `[tests/Reservas.IntegrationTests/RedisFixture.cs]`
- [x] **[Review][Defer] Eventos LWW no-op rotan el token igualmente** — `AplicarPrecio`/`AplicarDeshabilitada` descartados por versión igual invalidan la ciudad (misses de caché extra; solo eficiencia). `[Proyeccion/ProyectorCatalogo.cs]` — deferred.

_Descartados (ruido/falsos positivos):_ (1) "¿el ValidationBehavior corre para la query?" — sí: `Result<T>` implementa `IResultadoInvalidable`, el behavior aplica a `IRequest`, no solo `ICommand`. (2) `Impuestos ?? 0m` — inofensivo (hidratada garantiza el valor). (3) Caché no invalidada por cambios de Reservas (reserva/cancelación) — por diseño best-effort, aceptado en Task 3.

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

claude-opus-4-8 (bmad-dev-story, modo autónomo).

### Debug Log References

- RED: `dotnet test Reservas.UnitTests --filter BuscarDisponibilidad` → 2 fallos del decorador (`NotImplementedException`), 7 del validador en verde.
- GREEN: `dotnet test Reservas.UnitTests --filter BuscarDisponibilidad` → 9/9. Integración (`BusquedaDisponibilidadTests` + `CacheDisponibilidadTests`) → 9/9 (SQL + Redis reales).
- Regresión (suite completa): 23 (Reservas.Int) · 71 (Reservas.Unit) · 19 (Hoteles.Int) · 96 (Hoteles.Unit) · 15 (Comun.Web) · 9 (Contracts). 0 fallos.

### Completion Notes List

- **Query CQRS (Task 1/2):** `BuscarDisponibilidadQuery` es `IRequest<Result<IReadOnlyList<HabitacionDisponibleDto>>>` (NO `ICommand` → no pasa por `TransactionBehavior`). El filtro SQL cruza `ProyeccionHabitacion` (catálogo, filtrando por hidratada + estado "Habilitada" + capacidad) con `NochesHabitacion` (slots ocupados) exigiendo cero solapamiento en `[entrada, salida)` (semiabierto). Búsqueda sin resultados = 200 con lista vacía, no 404.
- **AC negativo (Task 2):** cubierto por tabla + facts: capacidad insuficiente, deshabilitada (incluye hotel deshabilitado, ya colapsado por 2.5 al estado "Deshabilitada"), otra ciudad, fila no hidratada, reservada-en-rango, solapamiento parcial; y el borde semiabierto adyacente que SÍ debe aparecer.
- **Caché (Task 3):** decoradora `BuscadorDisponibilidadCacheado` sobre `IDistributedCache` (Redis) con **claves generacionales por ciudad** — invalidar = rotar el token `disp:tok:{ciudad}` (O(1), sin escaneo). El `ProyectorCatalogo` invalida la ciudad afectada FUERA de la transacción del read-model (un fallo de Redis no revierte la proyección ya confirmada). TTL 30 s como red de seguridad best-effort para cambios de disponibilidad no invalidados de forma dirigida.
- **Endpoint (Task 4):** `GET /api/v1/habitaciones/disponibles` mapea `Result→ToOkResult`. Redis se registra si hay cadena "redis"; si no, `AddDistributedMemoryCache` para desarrollo local sin Aspire.
- **Deuda conocida heredada de 3.1:** una habitación rehabilitada podría seguir "Deshabilitada" si el re-alta no llega por eventos (asimetría 2.5, ya registrada en `deferred-work.md`) — fuera de alcance de 3.2.

### File List

**Nuevos (producción):**
- `src/Servicios/Reservas/Reservas.Application/Reservas/BuscarDisponibilidad/BuscarDisponibilidadQuery.cs`
- `src/Servicios/Reservas/Reservas.Application/Reservas/BuscarDisponibilidad/BuscarDisponibilidadQueryValidator.cs`
- `src/Servicios/Reservas/Reservas.Application/Reservas/BuscarDisponibilidad/BuscarDisponibilidadQueryHandler.cs`
- `src/Servicios/Reservas/Reservas.Application/Abstracciones/IBuscadorDisponibilidad.cs`
- `src/Servicios/Reservas/Reservas.Application/Abstracciones/ICacheDisponibilidad.cs`
- `src/Servicios/Reservas/Reservas.Infrastructure/Proyeccion/BuscadorDisponibilidadSql.cs`
- `src/Servicios/Reservas/Reservas.Infrastructure/Proyeccion/BuscadorDisponibilidadCacheado.cs`
- `src/Servicios/Reservas/Reservas.Infrastructure/Cache/CacheDisponibilidadRedis.cs`
- `src/Servicios/Reservas/Reservas.Infrastructure/Cache/OpcionesCacheDisponibilidad.cs`

**Modificados (producción):**
- `src/Servicios/Reservas/Reservas.Infrastructure/Proyeccion/ProyectorCatalogo.cs` (invalidación de caché por ciudad tras aplicar evento)
- `src/Servicios/Reservas/Reservas.Infrastructure/RegistroInfraestructura.cs` (DI del buscador + caché)
- `src/Servicios/Reservas/Reservas.Api/Program.cs` (registro de caché distribuida + endpoint)
- `src/Servicios/Reservas/Reservas.Infrastructure/Reservas.Infrastructure.csproj`, `.../Reservas.Api/Reservas.Api.csproj`, `Directory.Packages.props`, `tests/Reservas.IntegrationTests/Reservas.IntegrationTests.csproj`

**Nuevos (tests):**
- `tests/Reservas.UnitTests/Reservas/BuscarDisponibilidad/BuscarDisponibilidadQueryValidatorTests.cs`
- `tests/Reservas.UnitTests/Reservas/BuscarDisponibilidad/BuscadorDisponibilidadCacheadoTests.cs`
- `tests/Reservas.IntegrationTests/BusquedaDisponibilidadTests.cs`
- `tests/Reservas.IntegrationTests/CacheDisponibilidadTests.cs`
- `tests/Reservas.IntegrationTests/RedisFixture.cs`

**Modificados (tests):**
- `tests/Reservas.IntegrationTests/SqlServerFixture.cs` (colección "sqlserver" añade `RedisFixture`)

### Change Log

- 2026-07-09 — Story 3.2 implementada (TDD Red→Green). Query CQRS de disponibilidad sobre la proyección de 3.1 + caché de lectura Redis con invalidación generacional por ciudad. Endpoint `GET /api/v1/habitaciones/disponibles`. Suite completa en verde.
