---
baseline_commit: 5c035d187e3096177d5786f4265d7fdca75e01f7
---

# Story T.6: Paginación y caché Redis de la lectura del catálogo

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

> **Trazabilidad:** Hallazgo diferido del code-review de T.5 (instrucción de Santiago 2026-07-11) → performance/robustez de la lectura del catálogo → `AC-ET.6.x` · **Mejora (no obligatorio del enunciado)**
> **Porqué:** los GET de lista del catálogo (T.5) devuelven **todo** el catálogo del agente/hotel sin paginar (carga no acotada) y golpean SQL en cada lectura. Se añade **paginación** (acota la carga) y **caché Redis** (mejora el performance de lecturas repetidas), con **invalidación correcta en escrituras** (nunca servir datos stale tras crear/editar/eliminar).

## Story

Como **agente de viajes** con un catálogo grande,
quiero **listar mis hoteles y habitaciones paginados y con respuesta rápida**,
para **navegar el catálogo sin traer todo de golpe y sin latencia innecesaria en lecturas repetidas**.

## Acceptance Criteria

**AC-ET.6.1 — Paginación de `GET /api/v1/hoteles`**
Acepta `page` (≥1, default 1) y `pageSize` (1..100, default 20). Devuelve un sobre `PaginaDto<T>` con `items`, `page`, `pageSize`, `total` (conteo total del agente). `page`/`pageSize` fuera de rango → **400** (validación). El orden es estable (por Nombre). Sigue siendo `SoloAgente` y aislado por agente.

**AC-ET.6.2 — Paginación de `GET /api/v1/hoteles/{hotelId}/habitaciones`**
Igual contrato de paginación (`page`/`pageSize`/`PaginaDto`). Mantiene el 404 si el hotel es ajeno/eliminado/inexistente (antes de paginar). Orden estable (por Ubicación).

**AC-ET.6.3 — Caché Redis de las listas (cache-aside, Redis-si-configurado)**
Las dos listas se cachean en Redis vía `IDistributedCache` (mismo mecanismo que la caché de disponibilidad de Reservas, ADR-012). Clave por **agente/hotel + page + pageSize + generación** (ver AC-ET.6.4). TTL de respaldo (p. ej. 60 s). **Degradación**: si NO hay cadena de Redis configurada (tests unit/integración sin Redis), la lectura pasa directo a SQL (sin romper) — misma política "Redis-si-configurado" del almacén de idempotencia. Los **detalles** (item único por PK) NO se cachean (ya son baratos); el foco es las listas "sin filtro".

**AC-ET.6.4 — Invalidación correcta en escrituras (sin stale)**
Tras CUALQUIER escritura de hotel del agente (crear/editar/eliminar/habilitar/deshabilitar) la caché de la lista de **hoteles de ese agente** queda invalidada; tras cualquier escritura de habitación, la caché de la lista de **habitaciones de ese hotel** queda invalidada. Estrategia: **contador de generación por clave lógica** en Redis (`INCR` de `hoteles-gen:{agente}` / `habitaciones-gen:{hotelId}`); las claves de caché incluyen la generación vigente → un bump hace que las lecturas construyan una clave nueva (miss → SQL fresco), sin necesidad de borrado por patrón. **Criterio duro:** crear un hotel y luego `GET /api/v1/hoteles` **debe** mostrarlo inmediatamente (nunca la lista cacheada vieja) — verificado por test y por smoke/Newman.

**AC-ET.6.5 — Cableado + sin regresiones**
`ConnectionStrings__redis` cableado a Hoteles en `docker-compose.yml` (el servicio `redis` ya existe) y en `apps.tf` (ACA, ya hay Redis). Suite completa + G1 verdes. Postman/smoke: las listas usan `page`/`pageSize` y se verifica que crear→listar refleja el alta de inmediato (anti-stale).

## Tasks / Subtasks

- [x] **Task 1 — Paginación (contrato + queries)** (AC: ET.6.1, ET.6.2)
  - [x] `PaginaDto<T>` (record genérico: `Items`, `Page`, `PageSize`, `Total`) en `HotelBookingHub.Comun.Resultados`.
  - [x] `ListarHotelesDelAgenteQuery` y `ListarHabitacionesDeHotelQuery` pasan a llevar `Page`/`PageSize`; validador FluentValidation (page≥1, 1≤pageSize≤100) → `Result` 400. El endpoint bindea `page`/`pageSize` de query (con defaults).
  - [x] `ILectorCatalogo`: los métodos de lista devuelven `PaginaDto<...>` (con `Skip((page-1)*pageSize).Take(pageSize)` + `CountAsync` y orden estable `OrderBy(...).ThenBy(Id)` en `LectorCatalogoSql`). Detalle sin cambios.
  - [x] Endpoints: `MapGet` bindea `int page = 1`, `int pageSize = 20`.

- [x] **Task 2 — Caché Redis con invalidación por generación** (AC: ET.6.3, ET.6.4)
  - [x] Invalidador `IInvalidadorCacheCatalogo` (Abstracciones) con `InvalidarHotelesDeAgenteAsync(agente)` / `InvalidarHabitacionesDeHotelAsync(hotelId)`. **Desviación:** en vez de un `ICacheCatalogo` con factory, se usó un **decorator de `ILectorCatalogo`** (`LectorCatalogoCacheado`) → cache-aside sin cambiar handlers (Task 2 subtask 5 lo permitía).
  - [x] Impl Redis con **`IConnectionMultiplexer`** (no `IDistributedCache`): lee gen (`GET` clave-gen; ausente → 0), arma clave `catalogo:hoteles:{agente}:g{gen}:p{page}:s{size}`, get→hit / miss→SQL + `StringSet` con TTL. Invalidar = `StringIncrement` (INCR atómico) de la clave-gen. **Desviación justificada:** `IDistributedCache` no expone INCR atómico; la generación necesita `IConnectionMultiplexer`.
  - [x] **Degradación** "Redis-si-configurado": sin `ConnectionStrings:redis` se registra `InvalidadorCacheCatalogoNoop` + `LectorCatalogoSql` directo (sin caché); unit/integración sin Redis siguen verdes. Paquete `StackExchange.Redis` añadido a Hoteles.Infrastructure.
  - [x] Invalidación en el **write-path** (2 archivos): `HotelRepository` → `InvalidarHotelesDeAgenteAsync(hotel.AgentePropietario)` tras SaveChanges (crear + concurrencia); `HabitacionRepository` → `InvalidarHabitacionesDeHotelAsync(habitacion.HotelId)`. Invalidador inyectado (no-op si null).
  - [x] Los query-handlers de lista piden la lista al `ILectorCatalogo` inyectado → en presencia de Redis es el decorator cacheado (transparente para el handler).

- [x] **Task 3 — Cableado de infraestructura** (AC: ET.6.5)
  - [x] `deploy/docker-compose.yml`: `ConnectionStrings__redis: "redis:6379"` en `hoteles` + `depends_on: redis`. `apps.tf`: secret `cs-redis` + `ConnectionStrings__redis` en la app Hoteles (Redis gestionado ya existe).
  - [x] `RegistroInfraestructura` de Hoteles: registra `LectorCatalogoSql`, `OpcionesCacheCatalogo`, y si hay cadena Redis → `IConnectionMultiplexer` + `InvalidadorCacheCatalogoRedis` + `ILectorCatalogo`=`LectorCatalogoCacheado`; si no → Noop + `LectorCatalogoSql`.

- [x] **Task 4 — Tests (TDD)** (AC: ET.6.1-6.4)
  - [x] Unit: `PaginacionValidatorsTests` — `page`/`pageSize` fuera de rango → inválido (400 vía ValidationBehavior). `LecturaCatalogoTests.Paginacion_respeta_page_pageSize_y_total` (Total/Skip/Take).
  - [x] Integración (Testcontainers **Redis + SQL**, `CacheCatalogoTests`): (a) 2ª lectura = **hit** (una escritura sin invalidar no se ve); (b) **anti-stale**: crear hotel por el repo → listar lo muestra de inmediato; (c) aislamiento de caché por agente. `CacheCatalogoFixture` (SQL+Redis, conexión compartida).
  - [x] Aislamiento mantenido (clave por agente; `La_cache_esta_aislada_por_agente`).

- [x] **Task 5 — Artefactos + verificación** (AC: ET.6.5)
  - [x] Postman/smoke: las listas con `?page=1&pageSize=100`; asertan el sobre `PaginaDto` (items/total) y el anti-stale (crear→listar refleja). **Fix de robustez:** agente **único por corrida** en smoke y Postman (evita acumulación >100 hoteles que sacaría el alta de page 1) + `agenteEmail` del body de reserva alineado al agente del token (antes chocaba: crear guarda body.agenteEmail, cancelar compara token → 404).
  - [x] `dotnet build` (0 warnings) + `dotnet format` limpio + suite completa (489) + G1 (3, aislado) verdes + smoke/Newman(single 58 + `-n 2` 116) contra el compose **con Redis**.

## Dev Notes

### Naturaleza del trabajo
- Performance/robustez de lectura: paginación (bajo riesgo) + caché Redis con invalidación (riesgo = **staleness**; el criterio duro anti-stale de AC-ET.6.4 es el que hay que blindar con test). TDD.

### Decisiones de diseño (tomadas)
- **Paginación offset** (`page`/`pageSize`), no cursor: simple y suficiente para un catálogo de agente. Cap `pageSize ≤ 100`.
- **Invalidación por GENERACIÓN** (contador Redis por clave lógica), no borrado por patrón: `IDistributedCache` no permite scan/wildcard; el bump de generación deja las claves viejas inalcanzables (expiran por TTL). Correcto e inmediato tras la escritura (evita el stale que rompería "crear→listar lo muestra ya").
- **Redis-si-configurado** (degradación a pass-through sin Redis): unit/integración sin Redis siguen verdes; compose/ACA sí cablean Redis. Misma política que el almacén de idempotencia de Reservas.
- **Solo listas se cachean** (los detalles por PK ya son baratos y su invalidación sería otra familia de claves).
- **Invalidación en los repositorios** (2 archivos) tras SaveChanges, no en los 9 handlers → un solo lugar por agregado.

### Contexto verificado
- Patrón de caché existente: `Reservas.Application.Abstracciones/ICacheDisponibilidad`, `Reservas.Infrastructure/Cache/CacheDisponibilidadRedis.cs` (sobre `IDistributedCache`), `OpcionesCacheDisponibilidad`, decorator `BuscadorDisponibilidadCacheado`. DI "Redis-si-configurado" en `Reservas.Infrastructure/RegistroInfraestructura` (idempotencia + caché). Reusar el mismo enfoque en Hoteles.
- Los GET de T.5 usan el read-port `ILectorCatalogo` → `LectorCatalogoSql` (fail-closed de identidad ya en los handlers). La caché se intercala entre el handler y el lector (o dentro del lector-cacheado).
- Aislamiento por agente intacto: la clave de caché incluye el agente; la invalidación es por agente/hotel.
- `Habitacion` no tiene identidad de agente → la caché de habitaciones se invalida por `hotelId` (y el 404 por hotel ajeno se resuelve ANTES de tocar la caché, como en T.5).

### Project Structure Notes
- **Nuevos:** `PaginaDto<T>`, `ICacheCatalogo`/`IInvalidadorCacheCatalogo` (Abstracciones), `CacheCatalogoRedis` + pass-through (Infra), tests.
- **Modificados:** las 2 queries de lista + handlers, `ILectorCatalogo`/`LectorCatalogoSql` (paginación), endpoints (`Program.cs`), `HotelRepository`/`HabitacionRepository` (invalidación), `RegistroInfraestructura`, `docker-compose.yml`, `apps.tf`, Postman, `smoke.sh`, `Hoteles.Infrastructure.csproj` (paquete Redis).

### References
- [Source: src/Servicios/Reservas/Reservas.Infrastructure/Cache/CacheDisponibilidadRedis.cs] · [Source: .../Abstracciones/ICacheDisponibilidad.cs] — patrón cache-aside sobre IDistributedCache.
- [Source: src/Servicios/Reservas/Reservas.Infrastructure/RegistroInfraestructura.cs] — DI "Redis-si-configurado".
- [Source: docs/implementation-artifacts/t-5-lectura-del-catalogo-get-hoteles-habitaciones.md] — read-port ILectorCatalogo + fail-closed. [Source: ADR-012 Redis]

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Amelia / dev-story)

### Debug Log References

- E2E contra el compose descubrió dos bugs del **artefacto de prueba** (no del producto), ambos por el cap `pageSize<=100` + agente:
  1. `smoke.sh` usaba `pageSize=1000` → 400 (validación). Bajado a 100.
  2. Con agente fijo y compose persistente, el catálogo crecería sin límite y el alta caería fuera de page 1 → agente **único por corrida** (smoke + Postman). De paso destapó que `agenteEmail` del body de la reserva debía alinearse al agente del token (crear guarda `body.agenteEmail`; cancelar compara `token.AgenteActual` → 404 si difieren).

### Completion Notes List

- **Paginación** (AC-ET.6.1/.2): `PaginaDto<T>` en `Comun.Resultados`; queries con `Page/PageSize` + validador (page≥1, 1≤pageSize≤100); `LectorCatalogoSql` con `Skip/Take` + `CountAsync` y orden estable (`OrderBy(...).ThenBy(Id)`); endpoints bindean `page=1,pageSize=20`.
- **Caché Redis anti-stale** (AC-ET.6.3/.4): **decorator** `LectorCatalogoCacheado` sobre `ILectorCatalogo` (cache-aside, sin tocar handlers) + `IInvalidadorCacheCatalogo`. Invalidación por **generación** (`StringIncrement` de `catalogo:{hoteles-gen|habitaciones-gen}:{clave}`); las claves de página embeben la generación → un bump deja las viejas inalcanzables (miss → SQL fresco). Invalidación cableada en `HotelRepository`/`HabitacionRepository` (2 archivos) tras SaveChanges.
- **Desviaciones de diseño vs. la story (justificadas):** (a) decorator de `ILectorCatalogo` en vez de un `ICacheCatalogo` con factory — la subtarea de Task 2 lo permitía y no cambia handlers; (b) **`IConnectionMultiplexer`** en vez de `IDistributedCache` — la invalidación por generación necesita `INCR` atómico, que `IDistributedCache` no expone.
- **Degradación "Redis-si-configurado"**: sin `ConnectionStrings:redis` → `InvalidadorCacheCatalogoNoop` + `LectorCatalogoSql` directo (unit/integración sin Redis verdes; misma política que la idempotencia de Reservas).
- **Verificación**: unit (validators), integración con Testcontainers **Redis+SQL** (`CacheCatalogoTests`: hit real, anti-stale, aislamiento de caché por agente), suite completa + G1, y E2E smoke/Newman contra el compose con Redis (anti-stale live + propagación del evento al worker).

### File List

**Nuevos**
- `src/Comun/HotelBookingHub.Comun/Resultados/PaginaDto.cs`
- `src/Servicios/Hoteles/Hoteles.Application/Abstracciones/IInvalidadorCacheCatalogo.cs`
- `src/Servicios/Hoteles/Hoteles.Infrastructure/Cache/ClavesCacheCatalogo.cs`
- `src/Servicios/Hoteles/Hoteles.Infrastructure/Cache/InvalidadorCacheCatalogo.cs`
- `src/Servicios/Hoteles/Hoteles.Infrastructure/Cache/LectorCatalogoCacheado.cs`
- `tests/Hoteles.IntegrationTests/CacheCatalogoFixture.cs`
- `tests/Hoteles.IntegrationTests/CacheCatalogoTests.cs`
- `tests/Hoteles.UnitTests/ListarCatalogo/PaginacionValidatorsTests.cs`

**Modificados**
- `src/Servicios/Hoteles/Hoteles.Application/Abstracciones/ILectorCatalogo.cs` (listas → `PaginaDto`)
- `.../Hoteles/ListarHoteles/ListarHotelesDelAgenteQuery.cs` · `.../Habitaciones/ListarHabitaciones/ListarHabitacionesDeHotelQuery.cs` (Page/PageSize + validador)
- `src/Servicios/Hoteles/Hoteles.Infrastructure/Persistencia/LectorCatalogoSql.cs` (Skip/Take + Count + orden estable)
- `.../Persistencia/HotelRepository.cs` · `.../Persistencia/HabitacionRepository.cs` (invalidación tras SaveChanges)
- `src/Servicios/Hoteles/Hoteles.Infrastructure/RegistroInfraestructura.cs` (DI Redis-si-configurado)
- `src/Servicios/Hoteles/Hoteles.Infrastructure/Hoteles.Infrastructure.csproj` (StackExchange.Redis)
- `src/Servicios/Hoteles/Hoteles.Api/Program.cs` (endpoints page/pageSize + pasa cadena redis)
- `tests/Hoteles.IntegrationTests/Hoteles.IntegrationTests.csproj` · `tests/Hoteles.IntegrationTests/LecturaCatalogoTests.cs`
- `deploy/docker-compose.yml` · `deploy/terraform/apps.tf` (ConnectionStrings__redis a Hoteles)
- `deploy/scripts/smoke.sh` · `postman/hotel-booking-hub.postman_collection.json` (pageSize=100 + agente único por corrida + agenteEmail alineado)

## Change Log

| Fecha | Cambio |
|---|---|
| 2026-07-11 | Story T.6 creada (create-story): paginación (page/pageSize) de las listas GET del catálogo + caché Redis con invalidación por generación (anti-stale). Status → ready-for-dev. |
| 2026-07-11 | dev-story implementada: paginación + decorator de caché Redis (invalidación por generación), DI Redis-si-configurado, infra (compose+apps.tf), tests (unit+integración Testcontainers+E2E). Fix de artefactos (agente único por corrida + agenteEmail alineado). Suite 489 + G1 + smoke/Newman verdes. Status → review. |
