# Story 6.2: Autorización por rol (RBAC)

---
baseline_commit: 07319f08cb67cc248f4a57aad5623d58918e1a63
---

Status: review

<!-- Generado por bmad-create-story (modo autónomo, Épica 6). Complejidad NORMAL (policies .NET sobre los
claims que 6.1 dejó en HttpContext.User). TDD con Red→Green visible en el AC negativo (403). Depende de 6.1
(el token con claim `role` debe existir y validarse). El aislamiento por DATOS (agente↔agente) NO es esta
historia — es 6.3; aquí solo se decide "qué ROL puede invocar qué endpoint". -->

## Story

Como **operador del sistema**,
quiero **autorizar por rol (`Agente`/`Viajero`) en cada endpoint, resuelto server-side**,
para **que cada actor solo ejecute las operaciones de su rol (403 si no le corresponde)**.

## Acceptance Criteria

1. **AC-E6.2.1 — Rol sin permiso (AC negativo).** Dado un usuario **autenticado** con un rol que no tiene
   permiso para la operación, cuando la invoca, entonces responde **403** (policies .NET server-side). El cuerpo
   es Problem Details RFC 7807. (Nótese: autenticado pero no autorizado = 403, distinto del 401 de 6.1.)
2. **AC-E6.2.2 — Rol con permiso (happy path).** Dado un usuario con el rol correcto, cuando invoca la operación,
   entonces **NO** es rechazado por autorización (pasa a negocio).
3. **AC-E6.2.3 — Mapa de autorización explícito y completo.** Dado el conjunto de endpoints de negocio, cuando se
   inspecciona, entonces **cada uno** declara su policy de rol (ninguno queda con autorización implícita/abierta);
   el mapa rol→endpoint está documentado.

## Tasks / Subtasks

> **Task 0 (breve, decisión de mapeo — NO requiere party-mode salvo duda).** Fijar el mapa rol→endpoint a partir
> de las historias de dominio (quién es el actor en cada FR). Propuesta base (validar y ajustar):
>
> | Endpoint | Rol | Origen |
> |---|---|---|
> | `POST /api/v1/hoteles` y todo `hoteles/*`, `habitaciones/*` (gestión) | **Agente** | FR-1…7 (HU1) |
> | `GET /api/v1/habitaciones/disponibles` (búsqueda) | **Viajero** (y Agente) | FR-8 (HU2-1) |
> | `POST /api/v1/reservas` (crear-confirmar) | **Viajero** (y Agente en su nombre) | FR-9 |
> | `GET /api/v1/reservas` y `/{id}` (listado/detalle del agente) | **Agente** | FR-13 (HU1-5) |
> | `POST .../solicitud-cancelacion` (solicitar) | **Viajero** y **Agente** | FR-14 (dos vías) |
> | `POST .../cancelacion/resolucion` (resolver) | **Agente** | FR-16 |
> | `POST .../cancelaciones/atajo` (un paso) | **Agente** | FR-17 |
> | `GET .../cancelaciones-pendientes` | **Agente** | FR-17 |
>
> Casos "Agente puede hacer lo del Viajero en su nombre" → policy que admite ambos roles, NO abrir el endpoint.

- [x] **Task 1 — Policies de autorización (AC: 1, 2)** *(TDD: test de 403 primero — RED→GREEN)*
  - [x] `RolesAplicacion` (Agente/Viajero, claim canónico `role`) + `PoliticasAutorizacion` (`SoloAgente`, `AgenteOViajero`) + `AddAutorizacionPorRol` en `Comun.Web/Seguridad/AutorizacionPorRolExtensions.cs` (`RequireRole`).
  - [x] `SoloViajero` NO se creó: ningún endpoint es exclusivo del viajero (todos los suyos los puede hacer el agente en su nombre → `AgenteOViajero`). Nombres/roles canónicos en un único lugar (constantes).
  - [x] `RoleClaimType="role"` + `MapInboundClaims=false` en la validación JWT (cierra la deuda de naming de 6.1; evita el remapeo legacy `role`→URI que rompía `RequireRole`).
- [x] **Task 2 — Aplicar policy por endpoint (AC: 1, 2, 3)**
  - [x] `RequireAuthorization(PoliticasAutorizacion.*)` en cada `Map*`: Hoteles ×9 = `SoloAgente`; Reservas = `SoloAgente` (listado/detalle/resolución/atajo/pendientes) y `AgenteOViajero` (crear/solicitar/buscar).
  - [x] Ningún endpoint de negocio queda sin policy (verificado por el test estructural de cobertura).
- [x] **Task 3 — 403 como Problem Details (AC: 1)**
  - [x] El 403 lo emite el pipeline de `ProblemDetails` ya registrado por `AddManejoExcepcionesNegocio` en los servicios (`AddProblemDetails` + status code pages por defecto de ASP.NET) — coherente con el resto.
- [x] **Task 4 — Tests (AC: 1, 2, 3)**
  - [x] Funcional HTTP (`Reservas.FunctionalTests`, WebApplicationFactory sin DB): Viajero→`SoloAgente`→403; token sin rol→403; Agente/Viajero→`AgenteOViajero`→NO 403. Unit de las policies (`IAuthorizationService`) en `Comun.Web.UnitTests`.
  - [x] Test estructural de cobertura del mapa: enumera `EndpointDataSource` y falla si un endpoint `/api/**` no lleva `IAuthorizeData` (secure-by-default verificado en CI).
- [x] **Task 5 — Documentar el mapa rol→endpoint** en el README (sección "Autorización por rol — RBAC").
- [x] **Task 6 — Commits en rama `feature/6-2-autorizacion-por-rol-rbac` + PR a `develop`** (autor Santiago Renteria; sin trailers; `dotnet format` verde).

## Dev Notes

### Estado actual que esta historia toca (leer antes de codificar)

- **`Reservas.Api/Program.cs`** — 8 endpoints minimal API (`CrearReserva`, `SolicitarCancelacion`,
  `ResolverCancelacion`, `CancelarEnUnPaso`, `ListarCancelacionesPendientes`, `BuscarDisponibilidad`,
  `ListarReservasDelAgente`, `ObtenerReservaDetalle`). Ninguno tiene `RequireAuthorization` hoy. [Source: src/Servicios/Reservas/Reservas.Api/Program.cs]
- **`Hoteles.Api/Program.cs`** — endpoints de gestión de hotel/habitación (crear/editar/eliminar/habilitar/
  deshabilitar). Todos son operaciones de **Agente**. Ninguno autorizado hoy. [Source: src/Servicios/Hoteles/Hoteles.Api/Program.cs]
- 6.1 deja `HttpContext.User` con el claim `role` validado. Esta historia **solo** añade policies sobre esos claims.

### Arquitectura (fuente `architecture.md` + `security-and-quality.md`)

- **RBAC server-side** con policies nativas .NET; el agente solo gestiona *sus* hoteles/reservas. [Source: architecture.md#Authentication-&-Security]
- **Práctica OWASP #2 (A01 Broken Access Control):** AuthZ RBAC server-side. [Source: security-and-quality.md]
- **Mapeo excepción→HTTP:** sin permiso → **403** (distinto del 401). [Source: architecture.md#Implementation-Patterns]

### Frontera con 6.3 (importante — no mezclar)

- **6.2 = autorización por ROL** ("¿un Agente puede resolver cancelaciones?"). **6.3 = autorización por DATOS /
  aislamiento** ("¿este Agente puede tocar ESTA reserva, que es de otro agente?"). Un endpoint puede pasar 6.2
  (rol Agente) y aun así fallar 6.3 (recurso ajeno → 403/404). No resolver aislamiento por datos aquí.

### Anti-patrones a evitar

- Autorizar en el cliente o confiar en un flag del body para el rol (debe salir del claim del token, server-side).
- Dejar un endpoint de negocio sin policy (autorización implícita = agujero). El test de cobertura del mapa lo evita.
- Poner la lógica de RBAC de negocio en el Gateway (el Gateway autentica; el rol de negocio se decide en el servicio).
- Confundir 401 con 403 (autenticado-sin-permiso es 403).

### Testing

- Matriz rol×endpoint: al menos un endpoint por rol probado en ambos sentidos (rol correcto/incorrecto).
- Test estructural de cobertura de policies sobre `EndpointDataSource`.

### Project Structure Notes

- Policies/constantes de rol en el helper compartido de `Comun.Web` (mismo lugar que el wiring de auth de 6.1).
- MODIFICADOS: `Hoteles.Api/Program.cs`, `Reservas.Api/Program.cs` (RequireAuthorization por endpoint).

### References

- [epics.md — Story 6.2 (AC-E6.2.1)](../planning-artifacts/epics.md)
- [architecture.md — Authentication & Security](../planning-artifacts/architecture.md)
- [security-and-quality.md — práctica #2 (A01)](../specs/spec-hotel-booking-hub/security-and-quality.md)
- [Story 6.1](6-1-autenticacion-jwt-oidc.md) (provee el claim `role` validado)
- [Story 6.3](6-3-aislamiento-entre-agentes.md) (aislamiento por datos — no confundir con RBAC)

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (bmad-dev-story, modo autónomo).

### Debug Log References

- TDD Red→Green visible: `test(6.2)` rojo (policies) → `feat(6.2)` verde. Suite completa **416 tests en verde**; `dotnet format` limpio; build 0 warnings.
- Bug atrapado por el test funcional (no por el unit): el rol correcto daba 403 porque `JwtSecurityTokenHandler` remapea el claim `role`→URI larga (inbound claim mapping legacy) y `RoleClaimType="role"` no lo hallaba. Fix: `MapInboundClaims=false`. El unit test no lo detectó (construye el `ClaimsIdentity` con el claim ya en "role"); el funcional sí.

### Completion Notes List

- **Task 0 (mapa rol→endpoint):** confirmado sin party-mode (decisión de mapeo directa desde el actor de cada FR). `SoloViajero` innecesaria (todo lo del viajero lo hace el agente en su nombre → `AgenteOViajero`).
- **AC-E6.2.1** (rol sin permiso → 403): Viajero en endpoint `SoloAgente` → 403; token autenticado SIN rol → 403 (no 401). Verificado a nivel HTTP.
- **AC-E6.2.2** (rol correcto pasa): Agente y Viajero en `AgenteOViajero` → NO 401/403.
- **AC-E6.2.3** (mapa completo): test estructural sobre `EndpointDataSource` asegura que ningún `/api/**` queda sin `IAuthorizeData`.
- **Deudas de 6.1 cerradas aquí:** naming del claim de rol (`role` canónico + `RoleClaimType`) y secure-by-default (el test de cobertura hace de gate en CI, en vez de un `FallbackPolicy` que obligaría a tocar `MapDefaultEndpoints` compartido).
- **Frontera con 6.3:** el aislamiento por datos NO se toca aquí; sigue vía `IContextoAgente`/`X-Agente` hasta 6.3.

### File List

**Producción (nuevos):**
- `src/Comun/HotelBookingHub.Comun.Web/Seguridad/AutorizacionPorRolExtensions.cs` (RolesAplicacion + PoliticasAutorizacion + AddAutorizacionPorRol)

**Producción (modificados):**
- `src/Comun/HotelBookingHub.Comun.Web/Seguridad/AutenticacionJwtExtensions.cs` (`RoleClaimType="role"` + `MapInboundClaims=false`)
- `src/Servicios/Hoteles/Hoteles.Api/Program.cs` (AddAutorizacionPorRol + `SoloAgente` ×9)
- `src/Servicios/Reservas/Reservas.Api/Program.cs` (AddAutorizacionPorRol + policies por endpoint + `public partial class Program`)
- `README.md` (sección "Autorización por rol — RBAC")

**Tests (nuevos):**
- `tests/TestKit.Auth/` (proyecto: `TokenDePrueba` compartido, sin refs a servicios)
- `tests/Reservas.FunctionalTests/` (proyecto: `ReservasApiFactory` + `AutorizacionRbacTests` + `CoberturaAutorizacionTests`)
- `tests/Comun.Web.UnitTests/AutorizacionPorRolTests.cs`

**Tests (modificados):**
- `tests/Seguridad.FunctionalTests/` (`JwtTestFactory` y `AutenticacionGatewayTests` migrados a `TestKit.Auth`; csproj referencia TestKit.Auth)
- `HotelBookingHub.slnx` (alta de TestKit.Auth + Reservas.FunctionalTests)

### Change Log

- 2026-07-10 — Story 6.2 implementada. RBAC server-side: policies `SoloAgente`/`AgenteOViajero` (`Comun.Web`), aplicadas por endpoint en Hoteles (×9) y Reservas según el actor. Claim de rol canónico `role` + `MapInboundClaims=false` (cierra deuda de naming de 6.1). Tests HTTP (403/no-403) + cobertura estructural del mapa + unit de policies; nuevo `TestKit.Auth` compartido. Suite completa 416 tests en verde. TDD Red→Green visible.
