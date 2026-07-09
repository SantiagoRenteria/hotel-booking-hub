# Story 3.1: Proyección de habitaciones idempotente y ordenada

Status: ready-for-dev

<!-- Generado por bmad-create-story. Historia de ALTA COMPLEJIDAD y VITRINA de diseño arquitectónico
(party-mode Winston+John): sus AC NACEN como escenarios BDD Gherkin ejecutables (specs, no bullets),
porque describen PROPIEDADES DE SISTEMA DISTRIBUIDO (convergencia bajo desorden, idempotencia bajo reentrega,
reconciliación) que el código esconde. Además TDD real con ciclo Red→Green VISIBLE en commits. -->

## Story

Como **BC de Reservas**,
quiero **mantener una `ProyeccionHabitacion` que converge bajo reentrega y desorden de la mensajería**,
para **que la búsqueda de disponibilidad (3.2) no mienta sobre el inventario**.

Es el read-model de CQRS que combina **catálogo** (eventos de Hoteles de la Story 2.5) y **disponibilidad**
(slots `NochesHabitacion`, propiedad de Reservas). El productor 2.5 entrega **at-least-once** con order key
`(aggregateId=HabitacionId, version)` y head-of-line por agregado (single-instance); **la no-duplicación y la
convergencia bajo desorden son responsabilidad de ESTE consumidor** (arquitectura: "la no-duplicación se
garantiza en el consumidor, no en el wire").

## Acceptance Criteria (BDD · Gherkin ejecutable)

> Estos escenarios son la **especificación ejecutable** de la historia: cada uno se ata a un test (unit o
> integración con SQL/Redis reales) que un cambio incompatible rompe. No son documentación decorativa.

### AC-E3.1.0 — Dos proyecciones con dueño explícito (cierra el gap productor-sin-consumidor)

```gherkin
Feature: La ProyeccionHabitacion combina catálogo y disponibilidad

  Scenario: El read-model refleja atributos de catálogo y ocupación
    Dado los eventos de catálogo de E2 (HabitacionAgregada/PrecioHabitacionCambiado/HabitacionDeshabilitada,
      contrato de AC-E2.5.1) y el evento ReservaConfirmada de E1
    Cuando el proyector los consume
    Entonces la ProyeccionHabitacion combina atributos de catálogo (hotel, ciudad, tipo, costo, impuestos,
      capacidad, activa) y disponibilidad (noches ocupadas)
    Y ambos lados quedan disponibles para alimentar la búsqueda de AC-E3.2.1
```

### AC-E3.1.4 — Inbox de idempotencia compartido (habilitador, decidido aquí)

```gherkin
Feature: Un único mecanismo de deduplicación reutilizable por E3 y E5

  Scenario: El inbox registra el mensaje procesado de forma atómica
    Dado que E3 (proyección) y E5 (worker) deben deduplicar
    Cuando se procesa un mensaje por primera vez
    Entonces se registra su MessageId en el inbox (Redis SETNX + TTL) de forma atómica
    Y un segundo intento con el mismo MessageId encuentra la marca y NO reprocesa

  Scenario: El patrón de inbox es único y compartido
    Dado el mecanismo de dedup implementado aquí
    Cuando E5 (worker) necesite deduplicar en su historia
    Entonces reutiliza el MISMO patrón de inbox por MessageId (no una segunda tabla divergente)
```

### AC-E3.1.1 — Convergencia bajo desorden (la propiedad estrella)

```gherkin
Feature: La proyección converge al estado más reciente aunque los eventos lleguen desordenados

  Scenario: Un evento viejo que llega tarde no retrocede el estado
    Dado una habitación con PrecioHabitacionCambiado v2 ya aplicado (costo = 150)
    Cuando llega el PrecioHabitacionCambiado v1 tardío (costo = 100)
    Entonces el estado proyectado sigue siendo el de v2 (costo = 150)
    Y el evento v1 se descarta por order key (version <= última aplicada)

  Scenario Outline: El estado final es el de la versión más alta, sea cual sea el orden de llegada
    Dado la habitación <hab> sin proyectar
    Cuando los eventos <secuencia_de_llegada> se procesan en ese orden
    Entonces el estado final proyectado corresponde a la versión <version_final>

    Examples:
      | hab   | secuencia_de_llegada        | version_final |
      | HAB-1 | v1, v2, v3                  | v3            |
      | HAB-2 | v3, v1, v2                  | v3            |
      | HAB-3 | v2, v3, v1                  | v3            |
```

### AC-E3.1.2 — Idempotencia (sin duplicados bajo reentrega)

```gherkin
Feature: Reprocesar el mismo evento no duplica ni altera la proyección

  Scenario: El mismo evento entregado N veces deja la proyección intacta
    Dado el evento HabitacionAgregada (MessageId M, v1) ya proyectado
    Cuando el MISMO evento (MessageId M) se entrega 3 veces más
    Entonces filas_duplicadas == 0 en la ProyeccionHabitacion
    Y el estado proyectado es idéntico al de la primera entrega
```

### AC-E3.1.3 — Reconciliación / rebuild

```gherkin
Feature: La proyección puede reconstruirse a un estado correcto

  Scenario: Un rebuild converge la proyección corrupta o rezagada
    Dado una ProyeccionHabitacion corrupta, incompleta o rezagada
    Cuando corre el job de reconciliación/rebuild desde la fuente de verdad
    Entonces la proyección converge al estado correcto (mismos atributos que la fuente)
    Y el job es idempotente (correrlo dos veces da el mismo resultado)
```

## Tasks / Subtasks

> **Task 0 (party-mode, PRIMERO) — decisiones arquitectónicas.** Antes de tocar código, resolver con
> `/bmad-party-mode` (afectan contrato de consumo + almacenamiento del read-model + fuente de reconciliación):
> - **D1 — Transporte de los eventos de catálogo al consumidor.** El productor 2.5 usa `PublicadorEventosLog`
>   (placeholder, sin Dapr real). ¿Cómo recibe el proyector los eventos de Hoteles? Opciones: (a) suscripción
>   Dapr pub/sub real (endpoint de subscribe) con un broker/fake controlable; (b) bus in-proc/fake inyectable
>   que el relay publica y el proyector consume (permite tests deterministas de desorden/reentrega sin Dapr);
>   (c) el proyector lee del outbox como stream. Recomendación previa: (b) para el alcance (test-first,
>   determinista) dejando la sustitución por Dapr como adaptador. NO acoplar el dominio al transporte.
> - **D2 — Almacenamiento del read-model.** ¿`ProyeccionHabitacion` como tabla SQL en la BD de Reservas
>   (consultable por 3.2 con joins a `NochesHabitacion`) o en Redis? La arquitectura pone Redis como caché +
>   inbox + state store, y a Reservas como dueño del read-model. Recomendación previa: tabla SQL en Reservas
>   (la búsqueda 3.2 cruza disponibilidad con slots locales; Redis queda para el inbox y la caché de 3.2).
> - **D3 — Esquema del inbox y del tracking de orden.** Dedup por `MessageId` (Redis SETNX+TTL) + versión
>   última-aplicada por `aggregateId` (para descartar desorden). Confirmar TTL, y si el last-applied-version
>   vive en la fila de la proyección (columna `Version`) o en Redis. Recomendación: `Version` en la fila
>   (transaccional con el upsert; sobrevive al TTL del inbox).
> - **D4 — Fuente de la reconciliación (AC-E3.1.3).** No hay event-store durable cross-BC (el outbox de Hoteles
>   marca `Enviada` y no es log de eventos consultable por Reservas). ¿Rebuild desde: (a) re-solicitar snapshot
>   del catálogo a Hoteles vía un evento/endpoint de replay; (b) un applied-events log local en Reservas;
>   (c) reconstruir disponibilidad desde `NochesHabitacion` + último estado de catálogo conocido? Decisión
>   arquitectónica real; documentar alternativas antes de implementar.
> - Documentar alternativas/decisión y solo entonces implementar.

- [ ] **Task 1 — Contrato de consumo + inbox de idempotencia (AC: E3.1.4, E3.1.2)** *(TDD Red→Green)*
  - [ ] `IInbox` (o `IDeduplicador`) en `Reservas.Application`; implementación Redis `SETNX + TTL` por `MessageId` en `Reservas.Infrastructure/Idempotencia/`. Test de integración con Redis real (Testcontainers): primer `MessageId` procesa, repetidos no.
  - [ ] Escenario BDD `AC-E3.1.2` como test ejecutable (mismo evento ×N → 0 duplicados).
- [ ] **Task 2 — Proyector idempotente y ordenado (AC: E3.1.0, E3.1.1)** *(TDD + BDD)*
  - [ ] `ProyeccionHabitacion` (read-model, según D2) + `ProyectorHabitacion` que aplica los 3 eventos de catálogo; upsert con guarda de orden: descarta `version <= Version` aplicada por `aggregateId`.
  - [ ] Escenarios BDD `AC-E3.1.1` (incl. `Scenario Outline` de órdenes de llegada) como tests ejecutables contra SQL real.
  - [ ] Reemplazar el placeholder `DisponibilidadHabitacionSembrada` (1.6a) por la lectura de la proyección real; mantener verdes los tests de Reservas que lo usaban.
- [ ] **Task 3 — Transporte/consumo de eventos (AC: E3.1.0)** *(según D1)*
  - [ ] Adaptador de consumo (bus fake inyectable o suscripción Dapr) que entrega el envelope al proyector pasando por el inbox; el `data` llega como `JsonElement` (no castear al tipo concreto — patrón `PublicadorEventosLog`).
- [ ] **Task 4 — Job de reconciliación/rebuild (AC: E3.1.3)** *(según D4)*
  - [ ] Job idempotente que reconstruye la proyección a la fuente de verdad; test que parte de una proyección corrupta/rezagada y verifica convergencia.
- [ ] **Task 5 — Suite de propiedades distribuidas (Testcontainers SQL + Redis, colección aislada)**
  - [ ] Todos los escenarios Gherkin como tests; desorden/reentrega deterministas (sin `Task.WhenAll` ni timing real); colección `DisableParallelization` propia (patrón `OutboxFaultInjection`).
- [ ] **Task 6 — Commits TDD (Red→Green visibles) en rama `feature/3-1-proyeccion` + PR a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### BDD + TDD obligatorios (historia crítica/vitrina)

- **BDD:** los AC Gherkin de arriba son la spec ejecutable; cada `Scenario`/`Scenario Outline` se implementa como test nombrado por el escenario. Es donde el diseño distribuido se hace **verificable**, no prosa.
- **TDD:** ciclo **Red→Green visible en commits** (`test(3.1): ... (RED)` → `feat(3.1): ... (GREEN)`), no colapsado. [[tdd-critical-stories]] [[bdd-para-historias-complejas]].

### Arquitectura (fuente `architecture.md`)

- **Propiedad de datos:** Reservas es dueño de `NochesHabitacion` (slots) y de `ProyeccionHabitacion` (read-model por eventos de Hoteles). Hoteles dueño del catálogo. `Seq` nunca cruza el BC. [Source: architecture.md#Propiedad-de-datos]
- **Proyección idempotente y ordenada:** descarta eventos viejos por `version`/secuencia (evita "wrong forever" por reordenamiento). Job de reconciliación/rebuild como mitigante de corrupción. Disponibilidad de búsqueda filtrada best-effort (consistencia eventual; el invariante duro sigue en el motor de Reservas). [Source: architecture.md#Proyección]
- **Dedup en el consumidor** por `(MessageId, version)` — inbox Redis `SETNX + TTL`; descartar fuera de orden. **Un solo** patrón de inbox reutilizado por E5. [Source: architecture.md#446, #AC-E3.1.4]
- **Order key** = `data.aggregateId` (HabitacionId) + `version` del envelope. El productor 2.5 ya lo garantiza monotónico por agregado y con head-of-line single-instance; el consumidor cubre desorden cross-instance/cross-lote. [Source: 2.5 Review Findings]
- **Estructura:** `Reservas.Infrastructure/{Proyeccion/, Idempotencia/}` (dirs previstas, aún no existen). [Source: architecture.md#363]
- **Aislamiento de tests:** proyección/idempotencia con colección xUnit `DisableParallelization` + contenedor propio (SQL + Redis), estado reseteado por test; "broker caído" como fake controlable de Dapr, no tumbando infra real. [Source: architecture.md#403]

### Previous story intelligence (2.5 — el productor que alimenta esta proyección)

- Eventos de catálogo en `HotelBookingHub.Comun.Eventos`: `HabitacionAgregadaV1` (aggregateId, hotelId, tipoHabitacion, costoBase, impuestos, ubicacion, estado), `PrecioHabitacionCambiadoV1` (aggregateId, hotelId, costoBase, impuestos), `HabitacionDeshabilitadaV1` (aggregateId, hotelId). `Tipo` = `"X.v1"`. Envelope `EventoIntegracion { id, type, version, occurredAt, traceId, data }`; `Version` = order key (monotónica por habitación). Tras el outbox, `data` llega como `JsonElement`.
- **Asimetría de contrato heredada:** NO existe `HabitacionHabilitada` (habilitar no emite). Una habitación deshabilitada (v-n) y luego rehabilitada NO reaparece por eventos → la proyección debe contemplar que el re-alta de oferta es deuda de contrato (ver `deferred-work.md` · 2.5). Considerar en `AC-E3.1.0`/reconciliación.
- El productor `ProcesadorOutbox` de Hoteles hace head-of-line por agregado; el gemelo idempotencia+orden (v-repetida→no-op, v-vieja→descartada, v-siguiente→aplicada) es AC de ESTA historia (2.5 lo dejó anotado).

### Anti-patrones a evitar

- Deduplicar/ordenar en el productor y asumir el consumidor "limpio" (rompe la premisa at-least-once).
- Castear `data` al tipo concreto en el consumidor (llega como `JsonElement`).
- Proyección que retrocede ante evento viejo (rompe `AC-E3.1.1`).
- Dos mecanismos de inbox divergentes E3/E5 (rompe `AC-E3.1.4`).
- Tests de desorden con concurrencia real/timing (flakiness) en vez de barreras deterministas.
- Colapsar el ciclo TDD en un solo commit.

### Testing

- Unit: guarda de orden del proyector (version vs last-applied) y dedup del inbox (con fake de Redis o Redis real). Integración: SQL + Redis reales (Testcontainers) para convergencia/idempotencia/reconciliación; colección aislada.

### Project Structure Notes

- NUEVO `Reservas.Infrastructure/Proyeccion/` (read-model + proyector), `Reservas.Infrastructure/Idempotencia/` (inbox Redis). `Reservas.Application` (puertos `IInbox`, comando/handler de proyección). Reemplaza `Reservas.Infrastructure/Disponibilidad/DisponibilidadHabitacionSembrada.cs` (placeholder 1.6a). Posible migración para la tabla `ProyeccionHabitacion` (si D2 = SQL).

### References

- [epics.md — Story 3.1 (AC-E3.1.0/1/2/3/4)](../planning-artifacts/epics.md)
- [architecture.md — Proyección / inbox / order key / aislamiento de tests](../planning-artifacts/architecture.md)
- Story 2.5 (productor): `Comun.Eventos.*`, `Hoteles.Infrastructure/Outbox/ProcesadorOutbox.cs` (head-of-line), `deferred-work.md` (asimetría habilitar, backfill).
- Reservas 1.6b: outbox/relay como patrón de referencia; `DisponibilidadHabitacionSembrada`/`IDisponibilidadHabitacion` (placeholder a reemplazar).

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List

### Change Log
