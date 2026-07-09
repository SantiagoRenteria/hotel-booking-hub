# Story 2.5: Emisión del contrato de eventos de catálogo

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

Como **BC de Reservas (consumidor)**,
quiero **eventos de catálogo versionados y estables emitidos transaccionalmente por Hoteles**,
para **construir la proyección de disponibilidad de E3 sin acoplarme a Hoteles**.

> **Cierre de la Épica 2.** Aterriza dos cosas: (1) el **contrato de eventos de catálogo** (`HabitacionAgregada`, `PrecioHabitacionCambiado`, `HabitacionDeshabilitada`) con envelope versionado + order key, probado con snapshot (regla de propiedad de eventos: el productor los fija aunque E3 aún no los consuma); (2) el **write-path transaccional con outbox** para Hoteles —**saldando el diferido de 2.1**— reutilizando el patrón de Reservas (1.6b): `EjecutorTransaccional`/`TransactionBehavior` + tabla outbox + `RelayOutbox`.
> **Depende de 2.4** (las habitaciones son la fuente de los eventos): alta→`HabitacionAgregada`, cambio de costo/impuestos→`PrecioHabitacionCambiado`, deshabilitar→`HabitacionDeshabilitada`.
> **⚠️ Party-mode probable:** el contrato de eventos es cross-BC (lo consumirá Reservas en E3) y toca la arquitectura de mensajería; además convertir la escritura auto-contenida de Hoteles (2.1–2.4) en transaccional-con-outbox modifica el write-path ya implementado. Ambas son decisiones que afectan contratos/arquitectura → **evaluar party-mode** antes de fijarlas (forma del envelope/versionado, dónde viven los contratos, cómo se comparten con Reservas sin acoplar).

## Acceptance Criteria

1. **AC-E2.5.1 — Contrato de eventos (contract test).** Dado los eventos de catálogo publicados por Hoteles, cuando valido su serialización contra un snapshot, entonces cada uno lleva **envelope versionado** (`type` con semver, p. ej. `catalogo.habitacion-agregada.v1`) + **order key** `{ aggregateId, version }`; y un cambio incompatible **rompe el test**.
2. **AC-E2.5.2 — Emisión transaccional (at-least-once).** Dado un cambio de catálogo (alta / cambio de precio / deshabilitación de habitación), cuando se persiste, entonces el evento se escribe en el **outbox de Hoteles en la misma transacción** que el cambio (atomicidad: o ambos o ninguno). El relay entrega al menos una vez.
3. **AC-E2.5.3 — Solo los cambios que importan emiten.** Editar datos no económicos (tipo, ubicación) NO emite `PrecioHabitacionCambiado`; solo un cambio real de costo/impuestos lo hace. Habilitar no emite `HabitacionDeshabilitada`.

## Tasks / Subtasks

- [ ] **Task 0 — (Si aplica) Party-mode del contrato + write-path** — decidir: forma del envelope/versionado y order key; ubicación de los contratos (¿`Contracts`? ¿Hoteles.Domain/Contracts?) y cómo los lee Reservas sin acoplar; alcance del outbox de Hoteles (reusar `EjecutorTransaccional` de Reservas vs promover a `Comun`). Documentar alternativas/decisión.
- [ ] **Task 1 — Contratos de eventos de catálogo** — definir `HabitacionAgregada`/`PrecioHabitacionCambiado`/`HabitacionDeshabilitada` con envelope versionado (`type` semver) + order key `{ aggregateId, version }`. El aggregate `Habitacion` lleva una `Version` incremental (o se deriva del rowversion/secuencia) para el order key
- [ ] **Task 2 — Write-path transaccional de Hoteles (outbox)** — introducir el `EjecutorTransaccional`/`TransactionBehavior` + tabla `OutboxMessages` (Hoteles) + `RelayOutbox` (BackgroundService), reutilizando el patrón de Reservas 1.6b (mismo tratamiento de MessageId-once, reintentos, lease). Migración del outbox. Los handlers de 2.4 pasan a **stagear** el evento; la tx la cierra el ejecutor
- [ ] **Task 3 — Emisión desde los slices (AC: 2, 3)** — `CrearHabitacion`→`HabitacionAgregada`; `EditarHabitacion`→`PrecioHabitacionCambiado` **solo si** cambió costo/impuestos; `CambiarEstadoHabitacion`(→Deshabilitada)→`HabitacionDeshabilitada`. Encolar en el outbox dentro de la misma tx
- [ ] **Task 4 — Contract test (snapshot) (AC: 1)** — en `tests/Contracts`: serializar cada evento y compararlo contra un snapshot versionado; un cambio incompatible rompe el test. Verificar envelope + order key
- [ ] **Task 5 — Tests de atomicidad (AC: 2, 3)** — integración (Testcontainers): el cambio + el evento se persisten juntos (o ninguno ante fallo); editar datos no económicos NO emite `PrecioHabitacionCambiado`
- [ ] **Task 6 — Commit + push a `develop`** (autor Santiago Renteria; sin trailers)

## Dev Notes

### Regla de propiedad de eventos (por qué ahora)

- El productor (Hoteles) fija y prueba el contrato AUNQUE E3 no lo consuma todavía, para no reabrir el productor después. El contract test es el guardián: un cambio incompatible del evento rompe el build. La proyección consumidora es E3 (3.1).

### Reutilización del write-path de Reservas (1.6b) — saldar el diferido de 2.1

- Hoteles hoy escribe auto-contenido (INSERT/UPDATE directo en el repositorio). 2.5 lo convierte en el write-path transaccional unificado: `TransactionBehavior` (solo `ICommand`) abre tx → handler/repo stagean cambio + evento → `EjecutorTransaccional` hace `SaveChanges` + commit + reintento 1205 + MessageId-once. **Decidir en Task 0** si se reutiliza el `EjecutorTransaccional` de Reservas (¿promover a `Comun`?) o se replica en Hoteles. Ver `deferred-work.md` (write-path transaccional de Hoteles, diferido de 2.1).
- Clasificación de `SqlException` por `Number` (2627/2601/1205) como en Reservas; nunca por mensaje.

### Order key y versión del aggregate

- El order key `{ aggregateId, version }` permite a E3 ordenar/idempotentizar. `Habitacion` necesita una `Version` monotónica por aggregate (incrementada en cada cambio emitido). Evaluar derivarla de una columna dedicada vs del `rowversion` (el rowversion NO es monotónico global fiable como número de versión de negocio → preferible una `Version` explícita).

### Límites de alcance

- NO implementar la proyección/consumo (E3). NO publicar a un broker real más allá del relay ya existente (patrón 1.6b). El contrato y la emisión transaccional son el entregable.

### Anti-patrones a evitar

- Emitir el evento fuera de la transacción del cambio (rompe atomicidad → AC-E2.5.2).
- Contrato sin versionar o sin order key (rompe AC-E2.5.1 y el consumo de E3).
- Emitir `PrecioHabitacionCambiado` en ediciones que no cambian el precio (AC-E2.5.3).
- Clasificar excepciones por mensaje en vez de `SqlException.Number`.

### Testing

- Contract test en `tests/Contracts` (snapshot). Atomicidad en `tests/Hoteles.IntegrationTests` (Testcontainers, patrón de los tests de outbox de Reservas 1.6b: inyección de fallo para probar el «o ambos o ninguno»).

### Previous story intelligence

- Reservas 1.6b tiene el patrón completo: `EjecutorTransaccional`, `TransactionBehavior`, `ContextoMensajeria` (MessageId + conteo), `ColaOutbox`, `ProcesadorOutbox`, `RelayOutbox`, y sus tests de inyección de fallo. `tests/Contracts` ya existe (contrato del evento de reserva de 1.3). Reutilizar la forma del envelope versionado + order key de ahí.
- Habitaciones (2.4) son la fuente de los eventos; `CostoBase`/`Impuestos` son el «precio» de catálogo.

### Git

- Commit + push a `develop`; autor **Santiago Renteria**, sin coautoría IA.

### References

- [epics.md — Story 2.5](../planning-artifacts/epics.md) (AC-E2.5.1/2, NFR-3/NFR-8).
- [architecture.md — outbox transaccional / contrato de eventos / clasificación SqlException](../planning-artifacts/architecture.md).
- Stories 1.3 (contrato de evento + Contracts), 1.6b (write-path transaccional + outbox + relay), 2.4 (habitaciones, fuente de los eventos).
- [deferred-work.md](deferred-work.md) — write-path transaccional de Hoteles (diferido de 2.1); outbox multi-evento (1.6b).

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List

### Change Log
