# Story 6.3: Aislamiento entre agentes

---
baseline_commit: ed9cfdd18d45713956bc51d67dc0e47ccf3f9b2a
---

Status: done

<!-- Generado por bmad-create-story (modo autónomo, Épica 6). Complejidad ALTA → candidata a BDD
(Given/When/Then) además de TDD: cruza frontera BC (Reservas ya aísla por AgenteEmail; Hoteles NO tiene
noción de propietario), cierra la deuda IDOR registrada, y cambia la FUENTE de identidad de header X-Agente
al claim del token. Task 0 party-mode OBLIGATORIA: modelo de propiedad de Hoteles. Depende de 6.1 (claim de
identidad validado) y se apoya en 6.2 (rol Agente ya exigido). Validar con Winston+John si amerita BDD. -->

## Story

Como **agente**,
quiero **que mis hoteles y reservas sean invisibles e inmutables para otros agentes**,
para **proteger mi operación (control de acceso a nivel de DATOS, no solo de rol)**.

## Acceptance Criteria

1. **AC-E6.3.1 — Lectura ajena (AC negativo).** Dado un recurso (hotel/reserva) de **otro** agente, cuando
   intento leerlo, entonces responde **403/404 sin filtrar existencia** (no revela si el recurso existe).
2. **AC-E6.3.2 — Escritura ajena (AC negativo).** Dado un recurso de otro agente, cuando intento modificarlo,
   entonces responde **403** y el recurso **no** cambia (verificado tras el intento).
3. **AC-E6.3.3 — Identidad desde el token (cierra el puente `X-Agente`).** Dado una petición autenticada, cuando
   un handler resuelve la identidad del agente, entonces la toma del **claim del token validado** (6.1), **no**
   de la cabecera `X-Agente`; retirar el header como fuente de identidad. Fail-closed: sin claim de identidad → 403.
4. **AC-E6.3.4 — Cierra la deuda IDOR de cancelación.** Dado los endpoints de cancelación
   (`solicitud-cancelacion`, `cancelacion/resolucion`, `cancelaciones/atajo`), cuando un agente opera sobre una
   reserva que **no es suya**, entonces responde **403/404** (hoy cargan la reserva por `id` sin filtrar por
   `IContextoAgente`); y el campo de auditoría `Iniciador` no se acepta autodeclarado en contradicción con el
   principal autenticado. [Cierra `deferred-work.md`: "IDOR + Iniciador autodeclarado → Épica 6".]

## Tasks / Subtasks

> **✅ Task 0 RESUELTA (party-mode Winston/John/Amelia + decisión de Santiago, 2026-07-10) — Opción A.**
> Consenso unánime en **Opción A** (aprobada por Santiago). Decisiones:
> - **`AgentePropietario` en el agregado `Hotel` (dominio, no proyección)** — una proyección de solo-lectura no
>   puede autorizar *escrituras*; el caso duro de FR-24 (editar/eliminar hotel ajeno) se valida en el write model.
>   Es **aditivo** (no cambia invariantes; se fija una vez en `CrearHotel`, inmutable después).
> - **Autorización centralizada** — un único punto de aplicación (guard/spec) que atraviesen los 9 endpoints de
>   Hoteles, NO nueve `if` copiados (ahí es donde se colaría el bug). "Un solo lugar decide."
> - **Eje = email** (consistencia con `AgenteEmail` ya persistido en Reservas; documentado como decisión).
> - **Recurso ajeno → 404, no 403** (preserva el aislamiento a nivel de existencia; no filtra info entre inquilinos).
> - **Greenfield** (decisión de Santiago): columna **`NOT NULL`** desde el día uno; el dev-seed se re-siembra
>   asignando dueño. Sin backfill (no hay datos productivos).
> - **Dueño = el agente que crea el hotel.** Sin rol Admin (6.2 solo definió `Agente`/`Viajero`).
> - **Booking cross-BC FUERA de alcance (decisión de Santiago):** 6.3 cierra el aislamiento *dentro* de cada
>   contexto (gestión de Hoteles + listado/detalle/cancelación de Reservas). NO se restringe el booking a hoteles
>   propios (FR-24 no lo exige; rompería el modelo de agencia y el flujo del viajero). Pregunta de negocio futura.
> - **Secuencia TDD (Amelia), Red→Green visible:** (1) **IDOR Reservas PRIMERO** (P1 de seguridad: filtrar
>   cancelaciones por `IContextoAgente` → 404, derivar `Iniciador` del claim, quitar el campo autodeclarado del
>   request); (2) `X-Agente`→claim (nueva impl. de `IContextoAgente` desde `HttpContext.User`, migrar tests a
>   `TestKit.Auth`, sin tocar handlers/queries); (3) aislamiento Hoteles + `AgentePropietario` + guard centralizado;
>   (4) migración EF + seed con dueño; (5) re-verde suite E2.
>
> <details><summary>Contexto original de la decisión (opciones evaluadas)</summary>
>
> El problema: **Reservas ya aísla por `AgenteEmail`** (Story 3.3), pero **Hoteles NO modela propietario**.
> Opciones: **(A)** owner en `Hotel` + migración (RECOMENDADA/elegida); **(B)** acotar a Reservas + documentar
> (descartada: incumple la mitad "hoteles" de FR-24 dentro de la épica de seguridad); **(C)** proyección vía
> eventos (descartada: no autoriza escrituras, cruza BC, gold-plating).
>
> </details>

- [x] **Task 1 — Costura de identidad: header → claim (AC: 3)** *(refactor sin romper handlers)*
  - [x] Nueva impl. de `IContextoAgente` que lee el claim de identidad de `HttpContext.User` (email/sub) en vez de
    `X-Agente`. `HttpContextoAgente` (header) se retira o queda solo para tests/dev explícitamente marcado.
  - [x] Normalización canónica de la identidad idéntica a la existente (`Trim().ToLowerInvariant()`) para que el
    filtro por `AgenteEmail` siga siendo determinista e independiente del collation (patrón ya establecido en 3.3).
  - [x] Fail-closed: sin claim → `AgenteActual == null` → los handlers cortan con 403 (comportamiento ya presente).
- [x] **Task 2 — Cerrar IDOR en cancelaciones (AC: 4)** *(TDD: test de agente ajeno → 403/404 primero)*
  - [x] `SolicitarCancelacion`, `ResolverCancelacion`, `CancelarEnUnPaso`: cargar/filtrar la reserva por
    `IContextoAgente` (reserva ajena → 403/404, sin filtrar existencia). `ResolverCancelacion` ya tiene el guard
    de agente ajeno (AC-E4.2.4) — verificar que ahora la identidad viene del token; extender a solicitar/atajo.
  - [x] `Iniciador`: **cerrado por análisis (no por código) — corregido en review.** El guard de aislamiento
    (`reserva.AgenteEmail == agente`) impide que un no-dueño alcance el endpoint (un Viajero cae en 404), así que
    solo el agente **dueño** llega. Forzar `Iniciador == rol` **rompería FR-14** ("el agente puede iniciar en
    nombre del viajero" → `Iniciador=Viajero` legítimo con principal Agente). El IDOR (control de acceso) queda
    cerrado; `Iniciador` es metadato de auditoría declarado por el agente autorizado — no hay contradicción posible.
- [x] **Task 3 — Aislamiento de Hoteles (AC: 1, 2)** *(según Task 0; si (A))*
  - [x] `AgentePropietario` en `Hotel` + migración EF. `CrearHotel` lo setea desde el claim.
  - [x] Todas las operaciones de Hoteles (editar/eliminar/habilitar/deshabilitar hotel y habitación) filtran/
    autorizan por propietario server-side: recurso ajeno → 403/404 sin filtrar existencia; el recurso no cambia.
  - [x] Consultas por hotel/habitación respetan el mismo aislamiento.
- [x] **Task 4 — Tests (AC: 1, 2, 3, 4)** *(BDD si Task 0 lo confirma)*
  - [x] Dos agentes (A y B): B no ve ni edita recursos de A → 403/404; el recurso de A no cambia tras el intento de B.
  - [x] Identidad desde token (no header): petición con claim de A ve solo lo de A; sin claim → 403.
  - [x] IDOR de cancelación: agente ajeno sobre reserva de otro → 403/404 en los tres endpoints.
  - [x] Regresión: el aislamiento existente de listado/detalle/resolución (3.3, 4.2) sigue verde con la nueva fuente.
- [x] **Task 5 — Commits en rama `feature/6-3-aislamiento-entre-agentes` + PR a `develop`** (autor Santiago Renteria; sin trailers; `dotnet format`).

### Review Findings

_Code review 2026-07-10 (3 capas). Convergencia fuerte en un fail-open. 6 patch · 4 defer · varios dismiss. **Los 6 patch aplicados vía `/bmad-agent-dev` — suite 426 tests en verde, `dotnet format` limpio.** Fix clave: query filter fail-**closed** (flag "aislamiento activo"); claim `email` único; honestidad del checkbox `Iniciador` (cerrado por análisis); reconciliación 403/404 en epics.md + deferred-work; test de aislamiento a nivel handler._

- [x] **[Review][Patch] Query filter de Hoteles falla ABIERTO sin identidad** (ALTA, `blind+edge+auditor` — los 3) — `_agenteActual == null` desactivaba el filtro por propietario; un token con rol `Agente` pero SIN claim `email` (RBAC valida rol, no email) vería/mutaría los hoteles de todos (fail-open, contradice AC-E6.3.3 fail-closed). Fix: filtro **fail-closed** — flag "aislamiento activo" = ¿hay `IContextoAgente` inyectado? En el flujo HTTP (contexto presente) identidad null → `AgentePropietario == null` → no casa nada (404); el bypass queda SOLO para contextos sin costura (migración/design-time/siembra). `[HotelesDbContext.cs]`
- [x] **[Review][Patch] Claim `email` ambiguo → toma el primero** (BAJA, `blind+edge`) — el `HttpContextoAgente` retirado trataba multi-valor como ambiguo→null (fail-closed); `ClaimContextoAgente` perdió esa propiedad. Fix: si hay ≠1 claim `email` → null (fail-closed). `[Reservas.Api/ClaimContextoAgente.cs, Hoteles.Api/ClaimContextoAgente.cs]`
- [x] **[Review][Patch] Subtask `Iniciador` marcada hecha sin implementar** (ALTA-honestidad, `auditor`) — la Task 2 afirmaba "derivar/validar `Iniciador` contra el principal" pero no se hizo. **Resolución por análisis:** el guard de aislamiento (`reserva.AgenteEmail == agente`) impide que un no-dueño alcance el endpoint, y forzar `Iniciador==rol` **rompería FR-14** ("el agente inicia en nombre del viajero" → `Iniciador=Viajero` legítimo con principal Agente). El IDOR (control de acceso) está cerrado; `Iniciador` es metadato de auditoría declarado por el agente dueño autorizado. Fix: corregir el texto/checkbox de la subtask + `deferred-work.md` (no dejar un `[x]` falso). `[docs]`
- [x] **[Review][Patch] Spec auto-contradictoria: 403 vs 404** (MEDIA, `auditor`) — Task 0 (party-mode) eligió 404 para recurso ajeno, pero AC-E4.2.4/AC-E6.3.2/FR-16 en `epics.md` siguen diciendo 403. Fix: reconciliar esos AC/FR a "403/404 (404 elegido en 6.3)". `[epics.md]`
- [x] **[Review][Patch] `deferred-work.md` desactualizado** (BAJA, `auditor`) — la entrada "IDOR + Iniciador → Épica 6" no refleja que el IDOR ya se cerró. Fix: re-encuadrar. `[deferred-work.md]`
- [x] **[Review][Patch] Tests de aislamiento a nivel repo, no handler** (BAJA, `auditor`) — `AislamientoHotelesTests` verifica `ObtenerAsync`→null, no el 404 vía handler ni la no-mutación tras escritura ajena. Fix: añadir un test a nivel handler (agente B → EditarHotel → 404; hotel de A intacto). `[tests/Hoteles.IntegrationTests]`
- [x] **[Review][Defer] Backfill de hoteles legados** (MEDIA, `blind+edge`) — `AgentePropietario=''` en filas previas → invisibles para todo agente (fail-closed, correcto). Greenfield: inocuo. Al desplegar sobre datos existentes: backfill del propietario. Diferido (documentado en `deferred-work.md`).
- [x] **[Review][Defer] Dependencia del nombre/forma del claim `email` del IdP** (BAJA, `edge`) — un IdP externo (F2) podría emitir `emails`/URI/`preferred_username`. Hoy el emisor propio usa `email`. Al integrar IdP real: mapeo de claims configurable. Diferido.
- [x] **[Review][Defer] `_agenteActual` capturado en el ctor del DbContext** (BAJA, `blind+edge`) — verificado seguro hoy (DbContext scoped, sin pooling; se resuelve tras `UseAuthentication`). Latente si el pipeline cambiara. Diferido.
- [x] **[Review][Defer] Canal lateral de timing en el 404** (BAJA, `blind`) — "ajena" carga la reserva antes del 404; "inexistente" retorna inmediato. Micro-timing. Diferido.
- [x] **[Review][Dismiss] Filtro en todas las lecturas / IgnoreQueryFilters** (`blind`) — Hoteles no expone endpoints GET (Edge confirmó: sin queries); el único `IgnoreQueryFilters` es una aserción de test (cuenta filas físicas). Desestimado.

## Dev Notes

### Estado actual que esta historia toca (leer antes de codificar) — CRÍTICO

- **Reservas YA aísla por `AgenteEmail`** (Story 3.3, decisión party-mode "mis reservas"): `ListarReservasDelAgente`
  y `ObtenerReservaDetalle` filtran server-side vía `IContextoAgente`; `ResolverCancelacion` tiene guard de agente
  ajeno (AC-E4.2.4 → 403). El eje de aislamiento es **"reservas que el agente intermedió"**, no "hoteles que
  administra". [Source: 3-3-listado-de-reservas-del-agente-con-detalle.md, 4-2-resolver-cancelacion-con-auditoria.md]
- **`IContextoAgente` es la costura preparada para esta historia:** su doc dice "en la Épica 6 se reemplaza por
  un claim del token de auth **SIN tocar los handlers ni las queries**". Esta historia solo cambia la **impl.** de
  la costura (Task 1), no su interfaz. [Source: Reservas.Application/Abstracciones/IContextoAgente.cs]
- **`HttpContextoAgente` (header `X-Agente`)** ya es fail-closed y normaliza (`Trim().ToLowerInvariant()`), con
  test de header ambiguo. La nueva impl. debe conservar esas garantías leyendo del claim. [Source: HttpContextoAgente.cs]
- **DEUDA IDOR registrada (AC-E6.3.4):** los endpoints de cancelación cargan la reserva por `id` **sin** filtrar
  por `IContextoAgente` → un agente podría cancelar la de otro; y `Iniciador` es autodeclarado. Explícitamente
  diferido "→ Épica 6". [Source: deferred-work.md — code review 4.1]
- **Hoteles NO modela propiedad:** `Hotel`/`Habitacion` sin owner; endpoints sin filtro. Es el gap de la Task 0.
  [Source: src/Servicios/Hoteles/Hoteles.Api/Program.cs, Hoteles.Domain/*]

### Arquitectura (fuente `architecture.md`)

- **Aislamiento agente↔agente en autorización**, server-side; nunca confiar en un parámetro del cliente para la
  autorización de datos. [Source: architecture.md#Authentication-&-Security, AC-E3.3.2]
- **403/404 sin filtrar existencia:** no revelar si un recurso ajeno existe (evita enumeración). [Source: epics.md AC-E6.3.1]
- **Práctica OWASP #2 (A01):** "el agente solo gestiona *sus* hoteles/reservas". [Source: security-and-quality.md]

### Complejidad y método (memoria del proyecto)

- Historia de **alta complejidad** (cruza BC + cierra deuda de seguridad + cambia fuente de identidad). Candidata a
  **BDD** (Given/When/Then) además de TDD — **validar con Winston+John** en la Task 0 si lo amerita, como en 3.1/E4.
- Los AC negativos (403/404) se prestan a TDD Red→Green visible (test rojo de "agente ajeno" → verde con el filtro).

### Anti-patrones a evitar

- Filtrar por agente en el cliente o por un parámetro del body (rompe el aislamiento; debe ser server-side desde el claim).
- Cargar el recurso por `id` sin filtrar por propietario (IDOR — el bug exacto que esta historia cierra).
- Devolver el detalle/estado de un recurso ajeno (fuga de datos entre agentes).
- Revelar existencia con 404-vs-403 inconsistente que permita enumerar recursos ajenos.
- Aceptar `Iniciador` autodeclarado que contradiga el principal autenticado.
- Llamada síncrona Reservas→Hoteles para resolver el owner (romper la frontera BC — resolver local por claim/denormalización).

### Testing

- Integración (Testcontainers) con dos agentes: lectura/escritura cruzada → 403/404; recurso ajeno inmutable.
- IDOR de cancelación en los tres endpoints. Regresión del aislamiento existente (3.3/4.2) con la nueva fuente.

### Project Structure Notes

- Nueva impl. de `IContextoAgente` (claim) en `Reservas.Api` (y equivalente en `Hoteles.Api` si Task 0 = A).
- Si Task 0 = A: `AgentePropietario` en `Hoteles.Domain/Hoteles/Hotel.cs` + migración EF + filtros en handlers de Hoteles.
- MODIFICADOS: handlers de cancelación de Reservas (filtro por agente), endpoints de Hoteles.

### References

- [epics.md — Story 6.3 (AC-E6.3.1/2)](../planning-artifacts/epics.md)
- [architecture.md — Authentication & Security](../planning-artifacts/architecture.md)
- [deferred-work.md — IDOR + Iniciador autodeclarado (→ Épica 6)](deferred-work.md)
- [Story 3.3](3-3-listado-de-reservas-del-agente-con-detalle.md) (aislamiento por AgenteEmail ya existente)
- [Story 4.2](4-2-resolver-cancelacion-con-auditoria.md) (guard de agente ajeno AC-E4.2.4)
- [Story 6.1](6-1-autenticacion-jwt-oidc.md) (claim de identidad validado)

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (bmad-dev-story, modo autónomo). Decisión de diseño vía `/bmad-party-mode` (Winston/John/Amelia) + aprobación de Santiago.

### Debug Log References

- Secuencia TDD Red→Green visible (Task 0): (1) seam swap `X-Agente`→claim, (2) IDOR, (3) aislamiento Hoteles.
- Suite completa **424 tests en verde**; `dotnet format` limpio; build 0 warnings. Hoteles.IntegrationTests 22 (19 + 3 de aislamiento real sobre Testcontainers).
- Churn de firmas (predicho por Amelia): `Hotel.Crear` +owner y los 2 handlers de habitación +`IHotelRepository` rompieron ~9 archivos de test de Hoteles → reparados (subagente) sin cambiar aserciones.

### Completion Notes List

- **Task 0 (party-mode):** Opción A — `AgentePropietario` en el agregado `Hotel`. Ver la resolución completa en Task 0 (arriba) y `[[e6-hotel-ownership-decision]]`.
- **Task 1 (AC-E6.3.3):** identidad desde el claim `email` del token (`ClaimContextoAgente`), retira `X-Agente`/`HttpContextoAgente`. Handlers/queries intactos (costura `IContextoAgente`).
- **Task 2 (AC-E6.3.4, IDOR):** `SolicitarCancelacion` gana guard de aislamiento; los 3 endpoints de cancelación unifican recurso ajeno a **404** (antes 403 en Resolver/Atajo), sin filtrar existencia.
- **Task 3 (AC-E6.3.1/.2):** aislamiento de Hoteles por query filter global de `AgentePropietario` (centralizado — "un solo lugar decide"); habitaciones por su hotel padre. Migración EF greenfield NOT NULL.
- **Booking cross-BC:** fuera de alcance (decisión de Santiago); documentado.

### File List

**Producción (nuevos):**
- `src/Servicios/Reservas/Reservas.Api/ClaimContextoAgente.cs`
- `src/Servicios/Hoteles/Hoteles.Application/Abstracciones/IContextoAgente.cs`
- `src/Servicios/Hoteles/Hoteles.Api/ClaimContextoAgente.cs`
- `src/Servicios/Hoteles/Hoteles.Infrastructure/Migraciones/*_AislamientoAgentePropietarioHotel.cs` (+Designer, +snapshot)

**Producción (modificados):**
- `Reservas.Api/Program.cs` (registra ClaimContextoAgente), `Reservas.Application/Abstracciones/IContextoAgente.cs` (doc), `SolicitarCancelacion/ResolverCancelacion/CancelarEnUnPaso` handlers (guard + 404)
- `Reservas.Api/HttpContextoAgente.cs` (ELIMINADO)
- `Hoteles.Domain/Hoteles/Hotel.cs` (+AgentePropietario), `LongitudesHotel.cs` (+constante)
- `Hoteles.Infrastructure/Persistencia/HotelesDbContext.cs` (query filter por propietario + IContextoAgente)
- `Hoteles.Api/Program.cs` (registra IContextoAgente/ClaimContextoAgente)
- `Hoteles.Application/Hoteles/CrearHotel/...Handler.cs` (setea owner desde claim), `Habitaciones/{Editar,CambiarEstado}...Handler.cs` (visibilidad del hotel padre)

**Tests (nuevos):**
- `tests/Reservas.UnitTests/Reservas/ListarReservas/ClaimContextoAgenteTests.cs`
- `tests/Hoteles.IntegrationTests/AislamientoHotelesTests.cs`

**Tests (modificados):**
- `tests/Reservas.UnitTests/Reservas/ListarReservas/HttpContextoAgenteTests.cs` (ELIMINADO)
- Reparación de firmas + IDOR: SolicitarCancelacion (unit+int), Resolver/CancelarEnUnPaso (unit), ResolverCancelacion (int), ~9 archivos de Hoteles.UnitTests/IntegrationTests, `SqlServerFixture` (overload con agente), `AutorizacionRbacTests` (sin X-Agente)

### Change Log

- 2026-07-10 — Story 6.3 implementada (Opción A, party-mode). Aislamiento entre agentes: identidad desde el claim del token (retira X-Agente); IDOR de cancelación cerrado (404 ajeno); propiedad de Hoteles (`AgentePropietario` + query filter centralizado + migración). Booking cross-BC fuera de alcance. TDD Red→Green visible.
- 2026-07-10 — Code review (3 capas): convergencia en un fail-open del query filter. 6 patch aplicados vía agent-dev (fail-closed con flag de aislamiento activo; claim `email` único; `Iniciador` cerrado por análisis + honestidad del checkbox; reconciliación 403/404 en epics.md/deferred-work; test de aislamiento a nivel handler). 4 defer (backfill greenfield, dependencia del claim del IdP F2, captura en ctor verificada segura, timing BAJA). Suite 426 tests en verde, `dotnet format` limpio.
