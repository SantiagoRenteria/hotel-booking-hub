---
baseline_commit: 0232148
---

# Story 1.4: Cálculo de precio de la reserva (`CalculadorPrecio`)

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

Como **viajero**,
quiero **ver el precio total de la reserva calculado de forma correcta**,
para **saber cuánto pagaré antes de confirmar**.

> **Primer objetivo TDD del dominio.** Dominio **puro sin I/O** → 100% unit-testeable. Es el arranque del ciclo Red→Green→Refactor evidenciado en commits (lo que el enunciado valora). Depende de la Story 1.1 (esqueleto, `Reservas.Domain`). NO toca BD ni HTTP.

## Acceptance Criteria

1. **AC-E1.4.1 — Fórmula de precio.** Dado un `costoBase`, un `impuesto` y una `Estancia` de N noches, cuando se invoca `CalculadorPrecio`, entonces el total es `(costoBase + impuesto) × N` usando `decimal` (nunca `double`).

   | costoBase | impuesto | noches | total |
   |---|---|---|---|
   | 100.00 | 19.00 | 3 | 357.00 |
   | 80.00 | 0.00 | 1 | 80.00 |

2. **AC-E1.4.2 — Estancia inválida.** Dado una `Estancia` con `salida <= entrada`, cuando se construye el value object, entonces se rechaza con el mensaje `"La fecha de salida debe ser posterior a la de entrada"` (no se calcula precio).

## Tasks / Subtasks

- [x] **Task 1 — TDD del value object `Estancia` (AC: 2)** *(Red primero)*
  - [x] Test: `Estancia` con `salida <= entrada` → excepción/`Result` inválido con el mensaje exacto `"La fecha de salida debe ser posterior a la de entrada"`
  - [x] Test: número de noches = `(salida - entrada).Days` sobre `DateOnly`
  - [x] Implementar `Estancia` (VO con `DateOnly Entrada`, `DateOnly Salida`, factory con guard) en `Reservas.Domain`
- [x] **Task 2 — TDD de `CalculadorPrecio` (AC: 1)** *(Red primero)*
  - [x] Tests de la tabla de ejemplos (incluye impuesto 0, 1 noche, varias noches)
  - [x] Implementar `CalculadorPrecio` como **domain service puro** en `Reservas.Domain/Servicios/`: `(costoBase + impuesto) × noches`, todo en `decimal`
  - [x] Usar VO `Dinero` (`decimal`) si ya existe/procede; si no, esbozarlo mínimo
- [x] **Task 3 — Refactor** — limpiar sin romper tests (Green→Refactor); nombres tri-idioma
- [x] **Task 4 — Commit(s) que evidencien el ciclo TDD + push a `develop`** (Red, Green, Refactor visibles en el historial; autor Santiago Renteria)

## Dev Notes

### Diseño (fuente `architecture.md#Cálculo de precio`)

- **Domain service puro** `CalculadorPrecio` (sin I/O): `(costoBase + impuesto) × noches`. 100% unit-testeable → sostiene el TDD del flujo crítico.
- La penalidad de cancelación (0%/100%) es igualmente función pura, pero **eso es E4** — no la implementes aquí.

### Reglas de dominio

- **`decimal`** para todo dinero (nunca `double`/`float`) — fuente `patterns.md#Format`.
- **`DateOnly`** (`yyyy-MM-dd`) para las fechas de estancia; `DateTimeOffset` para timestamps (nunca `DateTime`).
- VO con **factory methods** y setters privados; guards que garantizan invariantes (fuente `stack-and-conventions.md`, `patterns.md#Factory`).
- Rango de estancia `[entrada, salida)` — la noche de `salida` no se cuenta (coherente con los slots de 1.5).

### Naming tri-idioma

- Dominio en español sin tilde: `Estancia`, `Dinero`, `CalculadorPrecio`. Mensajes de negocio con tilde: `"La fecha de salida debe ser posterior a la de entrada"`.

### Límites de alcance

- Solo dominio puro. NO `CrearReservaCommand` (1.6a), NO persistencia (1.5), NO slots. Si necesitas datos de habitación (costoBase/impuesto), recíbelos como parámetros — no los leas de BD.

### Anti-patrones a evitar

- `double`/`float` para dinero.
- `DateTime` en vez de `DateOnly`/`DateTimeOffset`.
- Lógica de precio acoplada a EF/repositorio (rompería la pureza y el TDD).

### Testing (fuente `delivery-and-testing.md`)

- `Reservas.UnitTests` (xUnit, puro/InMemory). Este es el corazón del TDD: **tests antes del código**, ciclo Red→Green→Refactor con commits que lo evidencien.
- Cobertura: happy + cada regla (impuesto 0, 1 noche, N noches, estancia inválida).

### Git

- Commits que muestren el ciclo TDD (p. ej. `test(reservas): ...` en rojo, luego `feat(reservas): ...` en verde); push a `develop`; autor **Santiago Renteria**.

### Project Structure Notes

- `src/Servicios/Reservas/Reservas.Domain/Servicios/CalculadorPrecio.cs`; VOs (`Estancia`, `Dinero`) en `Reservas.Domain/`. Tests en `tests/Reservas.UnitTests/`.

### References

- [epics.md — Story 1.4](../planning-artifacts/epics.md) (AC-E1.4.1…2).
- [architecture.md — Cálculo de precio](../planning-artifacts/architecture.md).
- [patterns.md](../specs/spec-hotel-booking-hub/patterns.md) (Factory, Format: decimal/DateOnly).
- [delivery-and-testing.md](../specs/spec-hotel-booking-hub/delivery-and-testing.md) (TDD del flujo crítico).

## Dev Agent Record

### Agent Model Used

Claude Opus 4.8 (claude-opus-4-8) vía bmad-dev-story.

### Debug Log References

- Ciclo TDD: **RED** (tests compilan-fallan por tipos inexistentes: `CS0234`/`CS0246`) → **GREEN** (build 0/0, `dotnet test` 6/6) → refactor (no requerido).
- `dotnet format --verify-no-changes` limpio; suite completa: Contracts 2/2, Reservas.UnitTests 6/6.

### Completion Notes List

- **AC-E1.4.1 ✅** — `CalculadorPrecio.Calcular(costoBase, impuesto, Estancia)` = `(costoBase + impuesto) × noches` en `decimal`. Tabla verificada: `(100.00, 19.00, 3) → 357.00`, `(80.00, 0.00, 1) → 80.00`.
- **AC-E1.4.2 ✅** — `Estancia.Crear` lanza `EstanciaInvalidaException` con el mensaje exacto `"La fecha de salida debe ser posterior a la de entrada"` cuando `salida <= entrada` (cubre `==` y `<`); no se calcula precio.
- Dominio **puro sin I/O** (unit-testeable): rango semiabierto `[Entrada, Salida)` → `Noches = Salida.DayNumber - Entrada.DayNumber` (coherente con el anti-solape de slots de 1.5). Dinero en `decimal`, fechas en `DateOnly`.
- VO con factory + setters privados (guard del invariante). `Dinero` no se introdujo (YAGNI; `decimal` cumple el patrón de dinero). Alimenta la Story 1.6a (happy path del comando).

### File List

- `src/Servicios/Reservas/Reservas.Domain/Reservas/Estancia.cs` (nuevo — VO)
- `src/Servicios/Reservas/Reservas.Domain/Reservas/EstanciaInvalidaException.cs` (nuevo)
- `src/Servicios/Reservas/Reservas.Domain/Servicios/CalculadorPrecio.cs` (nuevo — domain service puro)
- `tests/Reservas.UnitTests/Dominio/EstanciaTests.cs`, `tests/Reservas.UnitTests/Dominio/CalculadorPrecioTests.cs` (nuevos)

### Change Log

- 2026-07-08 · Story 1.4 · `CalculadorPrecio` + VO `Estancia` (dominio puro, TDD Red→Green): precio `(base+impuesto)×noches` y guard de rango con mensaje exacto. 6/6 tests. Estado: `in-progress` → `review`.
