---
baseline_commit: 506bff5
---

# Story 2.5: Emisión del contrato de eventos de catálogo

Status: in-progress

<!-- Generado por bmad-create-story (reemplaza el borrador manual previo). Historia de ALTA COMPLEJIDAD → TDD real (ciclo Red→Green visible en commits) + party-mode para las decisiones arquitectónicas señaladas. -->

## Story

Como **BC de Reservas (consumidor)**,
quiero **eventos de catálogo versionados y estables, emitidos transaccionalmente por Hoteles**,
para **construir la proyección de disponibilidad de E3 sin acoplarme a Hoteles**.

## Acceptance Criteria

1. **AC-E2.5.1 — Contrato de eventos (contract test).** Cada evento de catálogo (`HabitacionAgregada`, `PrecioHabitacionCambiado`, `HabitacionDeshabilitada`) se serializa con el **envelope versionado** `{ id, type, version, occurredAt, traceId, data }`, `type` = PascalCase + semver (`HabitacionAgregada.v1`), y **order key** = `data.aggregateId` (HabitacionId) + `version`. Un cambio incompatible (rename/quita de clave, semver, order key) **rompe el contract test** (rápido, sin contenedores, corre en cada PR).
2. **AC-E2.5.2 — Emisión transaccional (at-least-once).** Un cambio de catálogo (alta / cambio de precio / deshabilitación de habitación) escribe su evento en el **outbox de Hoteles en la MISMA transacción** que el cambio de dominio (o ambos o ninguno). El relay entrega al menos una vez.
3. **AC-E2.5.3 — Solo los cambios que importan emiten.** Editar datos no económicos (tipo, ubicación) NO emite `PrecioHabitacionCambiado`; solo un cambio real de `CostoBase`/`Impuestos` lo hace. Habilitar NO emite `HabitacionDeshabilitada`.
4. **AC-E2.5.4 — Order key monotónico.** El `version` del order key crece de forma monotónica por habitación en cada evento emitido, para que la proyección de E3 pueda ordenar/idempotentizar bajo reentrega y desorden.

## Tasks / Subtasks

> **✅ Task 0 RESUELTO (party-mode formal 2026-07-09 · Winston/Amelia/Murat):**
> - **D1 → (B) REPLICAR** un `EjecutorTransaccional`/`TransactionBehavior` **delgado** en `Hoteles.Infrastructure` (solo tx única READ COMMITTED + retry 1205 + `ChangeTracker.Clear` por intento; SIN traducción 2627→409, que es específica de Reservas). Compone con el rowversion existente: el ejecutor envuelve el `SaveChanges` que ya fija el `OriginalValue` del token; el `DbUpdateConcurrencyException` sube sin ser tocado por el retry de 1205. Voto 2-1 (Winston+Amelia por B; Murat por A con `IClasificadorErrores` inyectado + test de caracterización). Desempate del orquestador: menor riesgo/alcance (no tocar los 148 tests verdes de Reservas) para un 2º consumidor más simple; **el read-side genérico (`OutboxMessage`/`RelayOutbox`/`ProcesadorOutbox`) queda anotado como candidato a promover a `Comun` en la regla de tres**, no duplicado a la ligera.
> - **D2 → (a) UNÁNIME:** columna `Version` explícita (int/long) en `Habitacion`, monotónica por agregado, incrementada en la MISMA tx del outbox por cada mutación que emite evento. NO derivar del rowversion (global/opaco, chicken-and-egg con el post-save) ni de la Seq (orden global, no por-agregado). `Version` (stream de eventos) y `rowversion` (concurrencia) coexisten y miden cosas distintas.
> - **Tests exigidos por Murat (van en la implementación):** (i) monotonía en emisión — dos mutaciones consecutivas de la misma habitación emiten `version` estrictamente creciente y contigua; (ii) el gemelo de idempotencia+orden en E3 (v-repetida→no-op, v-vieja→descartada, v-siguiente→aplicada) queda como AC de E3, no de 2.5.
>
> **✅ D1 REFINADO (party-mode formal 2026-07-09 · Winston/Amelia/Murat, 3-0 → Opción U):** al implementar, apareció la fricción de composición no cerrada por D1=B: el `TransactionBehavior` corre el handler ANTES del `SaveChanges` del ejecutor, pero los 4 endpoints de habitación devuelven el `rowVersion` **post-`SaveChanges`** (Reservas no lo expone → por eso allí el patrón es limpio). Consenso unánime en **Opción U**: NO diferir el `SaveChanges` en Hoteles. El handler **stagea la fila de outbox en el MISMO `DbContext`** y el repositorio hace **un único `SaveChanges` (dominio + outbox) = transacción implícita ACID** → atomicidad (AC-E2.5.2) idéntica, `rowVersion` post-save devuelto como hoy, y la traducción `DbUpdateConcurrencyException→409` **se queda intacta en el repositorio** (cero regresión en la concurrencia optimista de 2.2/2.4). Se **NO** registra `TransactionBehavior`/`EjecutorTransaccional` en Hoteles. Winston: "U aterriza D1, no lo traiciona — la robustez debe ser proporcional al riesgo del BC (baja contención, filas únicas); el replay-1205 es cargo-cult aquí". Se pierde el replay-de-handler ante 1205 (aceptable; si se quisiera, retry a nivel `SaveChanges`). **El read-side genérico del outbox (`OutboxMessage`/`RelayOutbox`/`ProcesadorOutbox`) se replica delgado (2º consumidor); promoverlo a `Comun` queda para la regla de tres.**

<details><summary>Task 0 original (contexto de las alternativas evaluadas)</summary>

> **Task 0 (party-mode, PRIMERO) — decisiones arquitectónicas.** Antes de tocar código, resolver con `/bmad-party-mode` (afecta contratos + arquitectura de mensajería + write-path ya implementado):
> - **D1 — Alcance del write-path transaccional de Hoteles:** ¿reusar el `EjecutorTransaccional`/`TransactionBehavior`/`ColaOutbox`/`ContextoMensajeria`/`RelayOutbox`/`ProcesadorOutbox` de Reservas **promoviéndolos a `Comun`** (una sola implementación transversal), o **replicarlos** en `Hoteles.Infrastructure`? Trade-off: DRY/transversal vs acoplar dos BC a una pieza compartida. (La clasificación 2627/2601/1205 de Reservas es específica de su índice único; el de Hoteles no tiene overbooking, así que su `EjecutorTransaccional` es más simple — solo tx única + 1205 retry, sin 2627→409.)
> - **D2 — De dónde sale el `version` del order key:** columna `Version` monotónica explícita en `Habitacion` (incrementada por cada cambio emitido) vs derivar del `rowversion`/secuencia. Recomendación previa (Winston): `Version` explícita (el `rowversion` no es un número de versión de negocio fiable).
> - Documentar alternativas/decisión y solo entonces implementar.

</details>

- [x] **Task 1 — Contratos de eventos de catálogo (AC: 1, 4)** *(TDD: contract test RED primero)*
  - [x] En `HotelBookingHub.Comun.Eventos`: records `HabitacionAgregadaV1`, `PrecioHabitacionCambiadoV1`, `HabitacionDeshabilitadaV1` con su constante `Tipo`, reutilizando el envelope `EventoIntegracion`. `data.AggregateId` = `HabitacionId`. *(RED `b8a3e58` → GREEN `60d6d0f`)*
  - [x] `Habitacion.Version` (monotónica) según D2 — incrementada solo en mutaciones que emiten evento; migración `AgregaVersionYOutboxHabitaciones`. *(RED `e2c444a` → GREEN `300db49`)*
- [x] **Task 2 — Write-path transaccional de Hoteles (AC: 2)** *(D1 REFINADO → Opción U)*
  - [x] `IColaOutbox`/`ColaOutbox` (staging en el mismo DbContext) + `OutboxMessage` (Seq clustered + `UNIQUE(MessageId)` + lease `ReclamadoEn`) + `RelayOutbox` (BackgroundService, claim-then-publish, mark-sent solo tras publicar) + `ProcesadorOutbox` (tópico `hoteles`) + `PublicadorEventosLog`. Migración `AgregaVersionYOutboxHabitaciones`. **SIN `EjecutorTransaccional`/`TransactionBehavior`** (Opción U): el único `SaveChanges` del repositorio confirma dominio+outbox atómicamente. *(RED `56465cb` → GREEN `0739a8d`)*
  - [x] Registrar `IColaOutbox` + relay en `AddHotelesInfrastructure` (lo consume `Hoteles.Api`)
- [x] **Task 3 — Emisión desde los slices de 2.4 (AC: 2, 3)** *(TDD por slice)*
  - [x] `CrearHabitacion` → encola `HabitacionAgregadaV1` (v1) antes del `SaveChanges`
  - [x] `EditarHabitacion` → encola `PrecioHabitacionCambiadoV1` **solo si** cambió `CostoBase`/`Impuestos` (el dominio lo reporta)
  - [x] `CambiarEstadoHabitacion`(→Deshabilitada efectiva) → encola `HabitacionDeshabilitadaV1`; habilitar/idempotentes NO emiten
  - [x] Los handlers stagean el outbox; el `SaveChanges` del repositorio cierra la tx (Opción U) *(RED `e2c444a` → GREEN `300db49`)*
- [x] **Task 4 — Contract test (AC: 1, 4)** — `ContratoEventosCatalogoTests` (envelope + data + order key + semver). *(RED `b8a3e58` → GREEN `60d6d0f`)*
- [x] **Task 5 — Tests de atomicidad + selectividad (AC: 2, 3)** *(Testcontainers)*
  - [x] Integración: cambio + evento juntos o ninguno (fault-injection `InterceptorFallaOutbox`); relay at-least-once (T1/T2/T5/T6/T7). *(RED `56465cb` → GREEN `0739a8d`)*
  - [x] Selectividad (editar sin precio NO emite; habilitar NO emite) — unit `EmisionEventosCatalogoTests`
- [x] **Task 6 — Commit(s) TDD (Red→Green visibles) en la rama `feature/2-5-eventos-catalogo`; PR a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### TDD obligatorio (historia crítica/compleja)

- Ciclo **Red→Green visible en el historial**: commit `test(2.5): ... (RED)` con contract tests + tests de atomicidad fallando, luego `feat(2.5): ... (GREEN)` con la implementación. No colapsar tests+código en un solo commit.

### Reutilización del write-path de Reservas 1.6b (fuente canónica)

- `Reservas.Infrastructure`: `EjecutorTransaccional` (tx única READ COMMITTED + retry 1205 + `ChangeTracker.Clear()` por intento), `TransactionBehavior` (`MessageId = Guid.CreateVersion7()` UNA vez antes del retry; `ReiniciarConteo` por intento), `ColaOutbox` (guard "1 evento por comando" — reevaluar para 2.5, que puede emitir 1 evento por comando de habitación; si un comando llegara a emitir ≥2 revisar el diferido de outbox multi-evento en `deferred-work.md`), `OutboxMessage` (Seq clustered + `UNIQUE(MessageId)` + `ReclamadoEn` lease), `RelayOutbox`/`ProcesadorOutbox` (claim-then-publish, mark-sent solo tras publicar → at-least-once, nunca ==1). Clasificación por `SqlException.Number`, NUNCA por mensaje.
- **Diferencia clave para Hoteles:** no hay índice único de overbooking → el `EjecutorTransaccional` de Hoteles NO traduce 2627→409; solo tx única + 1205 retry. Esto refuerza la pregunta D1 (promover a `Comun` la parte común y dejar la clasificación específica por BC).

### Contrato de eventos (fuente `architecture.md` · `HotelBookingHub.Comun.Eventos`)

- Envelope `EventoIntegracion` `{ id, type, version, occurredAt, traceId, data }`; `type` PascalCase español + semver; `traceId` = `Activity.Current.TraceId` (W3C). Order key = `data.aggregateId` + `version`. JSON camelCase (STJ web defaults), enums string, `DateOnly` yyyy-MM-dd, dinero `decimal`.
- Regla de propiedad: el productor (Hoteles) fija y prueba el contrato aunque E3 aún no lo consuma → no reabrir el productor después.

### Condiciones heredadas del party-mode de la Épica 2 (para E3, NO implementar aquí)

- Orfandad de habitación (hotel eliminado en carrera): resolverse con un evento `HotelEliminado`/invariante con dueño + test de carrera real + guardia de escritura — es AC de E3, no de 2.5.
- Migración de la concurrencia a `ETag`/`If-Match` cuando E3 introduzca GET — deuda de contrato registrada.

### Límites de alcance

- NO proyección/consumo (E3). NO broker real más allá del relay (patrón 1.6b, publisher fake/log). El contrato + la emisión transaccional son el entregable.

### Anti-patrones a evitar

- Emitir el evento fuera de la transacción del cambio (rompe AC-E2.5.2).
- Contrato sin versionar o sin order key (rompe AC-E2.5.1/4).
- Emitir `PrecioHabitacionCambiado` en ediciones que no cambian el precio (AC-E2.5.3).
- Clasificar excepciones por mensaje en vez de `SqlException.Number`.
- Colapsar el ciclo TDD en un solo commit (regla de historias críticas).

### Testing

- Contract test en `tests/Contracts` (rápido, sin contenedores; patrón `ContratoReservaConfirmadaTests`). Atomicidad/selectividad en `tests/Hoteles.IntegrationTests` (Testcontainers + inyección de fallo, patrón `OutboxFaultInjection`/`OutboxAtomicidadYRelay` de Reservas).

### Previous story intelligence (2.4)

- Fuente de los eventos: `CrearHabitacion`/`EditarHabitacion`/`CambiarEstadoHabitacion` (handlers en `Hoteles.Application/Habitaciones/`). Hoy la escritura es auto-contenida (`HabitacionRepository` hace su propio `SaveChanges`); 2.5 la migra a stagear dentro del `EjecutorTransaccional`. `Habitacion` tiene `CostoBase`/`Impuestos` (el "precio" de catálogo) y `Estado`.
- `GuardarConcurrenciaAsync` (concurrencia optimista) debe convivir con el nuevo write-path: decidir en D1 cómo se compone la tx del outbox con el arbitraje del rowversion (probablemente el `EjecutorTransaccional` envuelve el `SaveChanges` que ya lleva el `OriginalValue` del token).

### Git

- Commit(s) TDD (Red→Green visibles) + push a `develop`; autor **Santiago Renteria**, sin coautoría IA.

### Project Structure Notes

- `Comun/HotelBookingHub.Comun/Eventos/` (nuevos records + Tipo). `Hoteles.Infrastructure/{Mensajeria,Outbox,Persistencia}/` (write-path + OutboxMessage + migración) — o `Comun`/`Comun`-adyacente según D1. `Hoteles.Application/Habitaciones/*` (emisión). `tests/Contracts/` (contract tests). `tests/Hoteles.IntegrationTests/` (atomicidad).

### References

- [epics.md — Story 2.5](../planning-artifacts/epics.md) (AC-E2.5.1/2, NFR-3/NFR-8).
- [architecture.md — Communication Patterns / envelope / MessageId / pipeline](../planning-artifacts/architecture.md).
- Reservas 1.6b: `EjecutorTransaccional`, `TransactionBehavior`, `ColaOutbox`, `OutboxMessage`, `RelayOutbox`, `ProcesadorOutbox`. Contrato 1.3: `tests/Contracts/ContratoReservaConfirmadaTests.cs`, `Comun.Eventos.EventoIntegracion`/`ReservaConfirmadaV1`.
- [deferred-work.md](deferred-work.md) — write-path transaccional de Hoteles (diferido de 2.1); outbox multi-evento (1.6b); condiciones de E3.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Amelia / dev-story). Decisiones arquitectónicas vía `/bmad-party-mode` (Winston/Amelia/Murat).

### Debug Log References

- Docker/Testcontainers disponible (28.4.0); `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04`.
- `BackgroundService` exigió `Microsoft.Extensions.Hosting.Abstractions` en `Hoteles.Infrastructure.csproj`.
- Migración autogenerada con CRLF/BOM/namespace en bloque → normalizada con `dotnet format`.

### Completion Notes List

- **D1 refinado a Opción U (party-mode 3-0):** sin `TransactionBehavior`/`EjecutorTransaccional` en Hoteles; el único `SaveChanges` del repositorio confirma dominio+outbox atómicamente (transacción implícita), preservando el contrato de `rowVersion` post-save de las respuestas y la traducción `DbUpdateConcurrencyException→409` intacta en el repositorio (cero regresión en 2.2/2.4).
- **TDD Red→Green visible** en 3 ciclos: contratos (`b8a3e58`→`60d6d0f`), dominio+emisión (`e2c444a`→`300db49`), outbox+integración (`56465cb`→`0739a8d`).
- `Habitacion.Version` cuenta solo las mutaciones emisoras (order key monotónico y contiguo, AC-E2.5.4). Habilitar NO emite (contrato de 3 eventos; el re-alta de oferta en E3 queda anotado como asimetría de contrato para E3).
- 190 tests verdes; build 0/0; `dotnet format` limpio.

### File List

- NUEVO `src/Comun/HotelBookingHub.Comun/Eventos/{HabitacionAgregadaV1,PrecioHabitacionCambiadoV1,HabitacionDeshabilitadaV1}.cs`
- NUEVO `src/Servicios/Hoteles/Hoteles.Application/Abstracciones/IColaOutbox.cs`
- NUEVO `src/Servicios/Hoteles/Hoteles.Infrastructure/{Mensajeria/ColaOutbox.cs,Mensajeria/PublicadorEventosLog.cs,Outbox/{OpcionesRelayOutbox,ProcesadorOutbox,RelayOutbox}.cs,Persistencia/OutboxMessage.cs}`
- NUEVO migración `Migraciones/20260709132200_AgregaVersionYOutboxHabitaciones.*`
- MOD `Hoteles.Domain/Habitaciones/Habitacion.cs` (Version + métodos que reportan cambio emisor)
- MOD `Hoteles.Application/Habitaciones/{CrearHabitacion,EditarHabitacion,CambiarEstadoHabitacion}/*Handler.cs` (emisión)
- MOD `Hoteles.Infrastructure/{Persistencia/HotelesDbContext.cs,RegistroInfraestructura.cs,Hoteles.Infrastructure.csproj}`
- NUEVO tests `Contracts/ContratoEventosCatalogoTests.cs`; `Hoteles.UnitTests/{ColaOutboxFake.cs,Habitaciones/{HabitacionVersionTests,EmisionEventosCatalogoTests}.cs}`; `Hoteles.IntegrationTests/{OutboxCatalogoTests,InterceptorFallaOutbox,PublicadoresFake}.cs` + fixture

### Change Log

- 2026-07-09 — Story 2.5 implementada (Opción U). Contratos + Version + write-path transaccional + emisión selectiva + tests de atomicidad/relay. Pendiente: `/bmad-code-review` y PR a `develop`.

### Review Findings (bmad-code-review · 2026-07-09)

Revisión adversarial de 3 capas (Blind Hunter / Edge Case Hunter / Acceptance Auditor) sobre el diff `506bff5..HEAD`. Los 4 AC quedaron verificados y probados; sin violaciones duras.

- [ ] **[Review][Decision] Reordenamiento intra-agregado ante fallo parcial de publicación → posible pérdida de evento** [Hoteles.Infrastructure/Outbox/ProcesadorOutbox.cs] — el `catch { … continue; }` deja que un evento posterior (Version mayor) del MISMO agregado se publique antes que uno anterior fallido; con eventos-delta, E3 podría descartar el reintento tardío (v-vieja) y perder el cambio. Hazard NUEVO de Hoteles (múltiples eventos por agregado) que Reservas no tenía (1 evento por reserva). Decidido en party-mode.
- [ ] **[Review][Patch] Falta test de atomicidad del camino UPDATE (Editar/CambiarEstado): 409/fallo descarta la fila de outbox staged** [tests/Hoteles.IntegrationTests/OutboxCatalogoTests.cs] — T2 solo cubre el INSERT (Crear). Correcto por construcción pero sin prueba directa del rollback en el UPDATE.
- [ ] **[Review][Patch] Endurecer T6 a cardinalidad exacta (`== 1`)** [tests/Hoteles.IntegrationTests/OutboxCatalogoTests.cs] — atraparía un doble-procesamiento en el mismo ciclo.
- [ ] **[Review][Patch] Unificar el acceso a traceId entre handlers** [Hoteles.Application/Habitaciones/*] — `CrearHabitacion` usa helper `TraceIdActual()`; Editar/CambiarEstado inlinean `Activity.Current?.TraceId.ToString()`. Cosmético.
- [x] **[Review][Defer] Migración `Version=0` en filas preexistentes (backfill de `HabitacionAgregada`)** [Hoteles.Infrastructure/Migraciones] — inocuo en greenfield; backfill al desplegar sobre datos existentes. Ver deferred-work.
- [x] **[Review][Defer] Sin dead-letter / tope de intentos en el relay (mensaje-veneno)** [Hoteles.Infrastructure/Outbox/ProcesadorOutbox.cs] — paridad con Reservas; límite operativo. Ver deferred-work.
- [x] **[Review][Defer] Reclamo del outbox no atómico (doble publicación con N réplicas del relay)** [Hoteles.Infrastructure/Outbox/ProcesadorOutbox.cs] — by-design at-least-once + instancia única + dedup E5. Ver deferred-work.
- Descartados (2): claim no atómico como *defecto* (es contrato at-least-once); `traceId` de ejemplo del contract test (sin impacto en AC).
