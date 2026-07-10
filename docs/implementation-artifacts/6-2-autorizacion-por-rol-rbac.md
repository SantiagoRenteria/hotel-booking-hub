# Story 6.2: Autorización por rol (RBAC)

Status: ready-for-dev

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

- [ ] **Task 1 — Policies de autorización (AC: 1, 2)** *(TDD: test de 403 primero)*
  - [ ] Definir policies nombradas en el helper compartido de `Comun.Web` (junto al de 6.1):
    `SoloAgente`, `SoloViajero`, `AgenteOViajero` (o `RequireRole`) — server-side, basadas en el claim `role`.
  - [ ] Registrar con `AddAuthorization(...)`; nombres de rol canónicos y en un único lugar (constantes) para
    evitar drift entre servicios (Gateway no decide RBAC de negocio; lo deciden los servicios).
- [ ] **Task 2 — Aplicar policy por endpoint (AC: 1, 2, 3)**
  - [ ] `RequireAuthorization("SoloAgente")` / etc. en cada `Map*` de `Hoteles.Api` y `Reservas.Api` según el mapa.
  - [ ] Verificar que **ningún** endpoint de negocio queda sin policy (auditar la lista completa de `Map*`).
- [ ] **Task 3 — 403 como Problem Details (AC: 1)**
  - [ ] El forbidden emite RFC 7807 coherente con el resto del sistema (no la respuesta vacía por defecto).
- [ ] **Task 4 — Tests (AC: 1, 2, 3)**
  - [ ] Por endpoint representativo de cada rol: token con rol incorrecto → `403`; token con rol correcto → NO 403.
  - [ ] Test de "cobertura del mapa": aserción de que cada endpoint de negocio tiene metadata de autorización
    (evita el endpoint olvidado sin policy). Puede ser un test que enumere `EndpointDataSource`.
- [ ] **Task 5 — Documentar el mapa rol→endpoint** en el README/AGENTS (tabla de Task 0 final).
- [ ] **Task 6 — Commits en rama `feature/6-2-autorizacion-por-rol-rbac` + PR a `develop`** (autor Santiago Renteria; sin trailers; `dotnet format`).

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

### Debug Log References

### Completion Notes List

### File List

### Change Log
