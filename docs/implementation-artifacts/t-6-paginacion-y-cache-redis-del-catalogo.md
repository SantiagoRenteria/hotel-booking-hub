---
baseline_commit: 5c035d187e3096177d5786f4265d7fdca75e01f7
---

# Story T.6: Paginación y caché Redis de la lectura del catálogo

Status: in-progress

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
  - [ ] `PaginaDto<T>` (record genérico: `Items`, `Page`, `PageSize`, `Total`) en Application (Comun o Hoteles.Application).
  - [ ] `ListarHotelesDelAgenteQuery` y `ListarHabitacionesDeHotelQuery` pasan a llevar `Page`/`PageSize`; validación (page≥1, 1≤pageSize≤100) — validador FluentValidation o guard → `Result` 400. El endpoint bindea `page`/`pageSize` de query (con defaults).
  - [ ] `ILectorCatalogo`: los métodos de lista devuelven `PaginaDto<...>` (con `Skip((page-1)*pageSize).Take(pageSize)` + `CountAsync` en `LectorCatalogoSql`). Detalle sin cambios.
  - [ ] Endpoints: `MapGet` bindea `int page = 1`, `int pageSize = 20`.

- [ ] **Task 2 — Caché Redis con invalidación por generación** (AC: ET.6.3, ET.6.4)
  - [ ] Read-port de caché en `Hoteles.Application.Abstracciones` (p. ej. `ICacheCatalogo` con `ObtenerOAgregarHotelesAsync`/`ObtenerOAgregarHabitacionesAsync`) + invalidador (`IInvalidadorCacheCatalogo` con `InvalidarHotelesDeAgente(agente)` / `InvalidarHabitacionesDeHotel(hotelId)`).
  - [ ] Impl Redis (`CacheCatalogoRedis`) sobre `IDistributedCache`: lee gen (`INCR`/`GET` de la clave-gen; si ausente, 0), arma la clave `hoteles:{agente}:g{gen}:p{page}:s{size}`, get→hit / miss→ejecuta el `factory` (SQL) y `SetAsync` con TTL. Invalidar = `INCR` de la clave-gen (bump). Serialización JSON del `PaginaDto`.
  - [ ] **Degradación**: DI "Redis-si-configurado" — si `ConnectionStrings:redis` vacío, registrar un cache pass-through (ejecuta el factory, sin cachear) para que unit/integración sin Redis funcionen. Añadir `Microsoft.Extensions.Caching.StackExchangeRedis` a Hoteles (ya usado en Reservas).
  - [ ] Cablear la invalidación en el **write-path**: los repositorios (`HotelRepository`/`HabitacionRepository`) invalidan tras un `SaveChanges` exitoso (localizado en 2 archivos, no en los 9 handlers): hotel write → `InvalidarHotelesDeAgente(hotel.AgentePropietario)`; habitación write → `InvalidarHabitacionesDeHotel(habitacion.HotelId)`. Inyectar el invalidador (no-op si no hay Redis).
  - [ ] Los query-handlers de lista pasan a pedir la lista **a través de la caché** (decorator o el handler llama al cache que envuelve al lector).

- [ ] **Task 3 — Cableado de infraestructura** (AC: ET.6.5)
  - [ ] `deploy/docker-compose.yml`: `ConnectionStrings__redis: "redis:6379"` en el servicio `hoteles` (+ `depends_on: redis`). `apps.tf`: `ConnectionStrings__redis` a Hoteles (Redis gestionado ya existe; ver cómo lo hace Reservas).
  - [ ] `RegistroInfraestructura` de Hoteles: registrar cache + invalidador (Redis-si-configurado), `AddStackExchangeRedisCache` cuando haya cadena.

- [ ] **Task 4 — Tests (TDD)** (AC: ET.6.1-6.4)
  - [ ] Unit/handler: validación de `page`/`pageSize` (fuera de rango → 400); `PaginaDto.Total` correcto; `Skip/Take` correcto.
  - [ ] Integración (Testcontainers **Redis** + SQL, como `Notificaciones.IntegrationTests`/`Reservas.IntegrationTests`): (a) segunda lectura idéntica = **hit** (no golpea SQL — verificable por generación/contador o por comportamiento); (b) **anti-stale**: crear hotel → listar → aparece de inmediato (la invalidación por gen funcionó); (c) paginación real (page 1 vs 2, total). Usar datos únicos por test (BD compartida + índice único).
  - [ ] Aislamiento se mantiene (la clave incluye el agente; un agente no ve la caché de otro).

- [ ] **Task 5 — Artefactos + verificación** (AC: ET.6.5)
  - [ ] Postman/smoke: las listas con `?page=1&pageSize=...`; asertar el sobre `PaginaDto` (items/total) y el anti-stale (crear→listar refleja). Newman single + `-n 2` verdes; smoke x1 verde.
  - [ ] `dotnet build` (0 warnings) + `dotnet format` limpio + suite completa + G1 verdes (G1 aislado, sin el compose compitiendo).

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

### Completion Notes List

### File List

## Change Log

| Fecha | Cambio |
|---|---|
| 2026-07-11 | Story T.6 creada (create-story): paginación (page/pageSize) de las listas GET del catálogo + caché Redis con invalidación por generación (anti-stale). Status → ready-for-dev. |
