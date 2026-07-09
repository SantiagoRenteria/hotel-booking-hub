# Spikes de Sprint 0 — referencia y decisión (Story 1.2)

Artefacto **persistente** del spike desechable de la Story 1.2. El código del spike vivió fuera del repo (throwaway) y se eliminó; aquí quedan la **decisión go/no-go**, los **resultados** y los **snippets de referencia** que alimentan la Story 1.5 (arbitraje) y la 1.6b (mediator + outbox).

## Decisión: **GO** en ambos (sin Plan B)

| Spike | Resultado | Veredicto |
|-------|-----------|-----------|
| Arbitraje por índice único (ADR-016) | N=50 concurrentes → **1 confirmada**, **49** por conflicto de único (2627/2601), **0** deadlock, **0** sin clasificar; **1 sola fila** en la tabla | ✅ GO — el motor arbitra; cero overbooking |
| Wiring del mediator (ADR-018) | Orden `Logging → Validation → Transaction → Outbox → Handler`; dominio + outbox en **un `SaveChangesAsync`** (2 filas); rollback → nada | ✅ GO — el patrón compone y es atómico |

No se activa el criterio de aborto: **no** hace falta caer a `SERIALIZABLE`. READ COMMITTED + índice único + clasificación por `Number` es viable para la Story 1.5.

**Nota sobre deadlocks (1205):** en la corrida de N=50 sobre inserciones de una sola fila con la misma clave, **el ganador se decide por violación de PK (2627), no por deadlock** → se observaron 0×1205. Es el comportamiento nominal para este patrón; la ruta de clasificación y retry de 1205 queda **codificada y reservada** para escenarios multi-noche/hotspot (la Story 1.5/1.6c la ejercitan con inserciones batch).

## Entorno validado

- **.NET 10** + **Testcontainers.MsSql 4.13** sobre `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04` (SQL Server real; la concurrencia y la unicidad NO se prueban con InMemory).
- `Microsoft.Data.SqlClient` para leer `SqlException.Number`; EF Core 10 + SQLite para la atomicidad del mediator.

## Snippet 1 — Clasificación de `SqlException` por `Number` (NUNCA por `.Message`)

Para la Story 1.5 (traducción a HTTP) y el `TransactionBehavior` (retry):

```csharp
// 2627/2601 → conflicto de único → 409 inmediato, SIN retry (otro ganó, determinístico).
// 1205      → víctima de deadlock → reintentable (retry acotado con backoff+jitter).
static string Clasificar(SqlException ex) => ex.Number switch
{
    2627 or 2601 => "conflicto-unico",
    1205         => "deadlock",
    _            => "otro",
};
```

## Snippet 2 — INSERT arbitrado bajo READ COMMITTED

```csharp
await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.ReadCommitted);
// INSERT en NochesHabitacion (PK/UNIQUE (HabitacionId, Noche)); si otro ya insertó esa noche → SqlException 2627.
await insert.ExecuteNonQueryAsync();
await tx.CommitAsync();
// Perdedores: catch (SqlException ex) → Clasificar(ex) → 409 (unico) | retry (1205).
```

DDL confirmado: `CONSTRAINT PK_NochesHabitacion PRIMARY KEY (HabitacionId, Noche)` — la clave compuesta ES el árbitro (coherente con ADR-016/017; en 1.5 la PK lógica es UUID v7 no-clustered y `NochesHabitacion` clustered por el compuesto).

## Snippet 3 — Pipeline del mediator (composición anidada por decorators)

Orden canónico verificado (Logging el más externo, Handler al centro):

```csharp
Func<Task<TResponse>> pipeline = () => handler.Handle(cmd, ct);
foreach (var b in behaviors.Reverse())     // behaviors en orden: Logging, Validation, Transaction, Outbox
{
    var siguiente = pipeline;
    var actual = b;
    pipeline = () => actual.Handle(cmd, siguiente, ct);
}
var resultado = await pipeline();
// orden ejecutado: Logging → Validation → Transaction → Outbox → Handler
```

Firma: `Task<TResponse> Handle(TRequest, CancellationToken)` con `TResponse = Result/Result<T>`. En 1.6b esto se registra por scan de assembly en un único `AddMediatorPipeline()`; `TransactionBehavior` solo comandos.

## Snippet 4 — Dominio + Outbox en el mismo `SaveChangesAsync` (ADR-018, atomicidad)

```csharp
db.Reservas.Add(new Reserva());                                   // cambio de dominio
db.Outbox.Add(new OutboxMessage { Type = "ReservaConfirmada.v1" }); // fila de outbox
var afectadas = await db.SaveChangesAsync();   // == 2: ambas en la misma transacción implícita
// Si la transacción que lo envuelve se revierte → NINGUNA de las dos persiste (verificado).
```

Regla no negociable para 1.6b: el insert a `OutboxMessages` va en el **mismo** `SaveChangesAsync` que el cambio de dominio; el `MessageId` se asigna **una vez antes** del loop de retry 1205 (para que `UNIQUE(MessageId)` dedupe de verdad).

## Qué alimenta

- **Story 1.5** — arbitraje 2627/1205 + DDL de `NochesHabitacion` + traducción a 409/retry.
- **Story 1.6b** — `TransactionBehavior` con retry 1205 + outbox en el mismo `SaveChanges` + `AddMediatorPipeline`.
