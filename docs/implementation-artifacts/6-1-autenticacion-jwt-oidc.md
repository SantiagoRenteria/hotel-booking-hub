# Story 6.1: Autenticación JWT/OIDC

Status: ready-for-dev

<!-- Generado por bmad-create-story (modo autónomo, Épica 6). Complejidad ALTA (infra de seguridad
transversal + topología de validación en Gateway y servicios). TDD con Red→Green visible en los AC
negativos (401). Task 0 party-mode: TOPOLOGÍA de validación del token (Gateway-only vs Gateway+servicios)
y EMISOR de tokens para el alcance de la prueba (sin IdP externo). Es la primera historia de la épica:
habilita 6.2 (RBAC lee el rol del token) y 6.3 (aislamiento lee la identidad del token, cerrando el puente X-Agente). -->

## Story

Como **operador del sistema**,
quiero **que toda operación exija un token JWT válido (issuer/audience/expiración/firma verificados)**,
para **impedir el acceso no autenticado (401)**.

## Acceptance Criteria

1. **AC-E6.1.1 — Sin token (AC negativo).** Dado una petición sin token, o con token inválido/expirado/mal
   firmado, cuando llega al Gateway, entonces responde **401** (issuer, audience, expiración y firma
   verificados). El cuerpo es Problem Details RFC 7807 (`WWW-Authenticate: Bearer` en la respuesta).
2. **AC-E6.1.2 — Token válido pasa (happy path).** Dado una petición con un JWT válido (issuer/audience
   esperados, no expirado, firma correcta), cuando invoca una operación existente, entonces **NO** es
   rechazada por autenticación (el resultado depende de autorización/negocio, no de 401).
3. **AC-E6.1.3 — Cero secretos en el repo.** Dado el repositorio, cuando se inspecciona, entonces la clave de
   firma / configuración sensible **no** está hardcodeada (user-secrets/Dapr/env en dev; Key Vault en nube);
   `appsettings.json` solo lleva valores no sensibles (issuer/audience) o placeholders. gitleaks en CI → `0` hallazgos.
4. **AC-E6.1.4 — El token propaga identidad y rol como claims.** Dado un token emitido para el alcance de la
   prueba, cuando un servicio lo valida, entonces expone en `HttpContext.User` los claims de **identidad del
   agente** (email/sub) y **rol** (`Agente`/`Viajero`) — base de 6.2 (RBAC) y 6.3 (aislamiento).

## Tasks / Subtasks

> **⚠️ Task 0 (party-mode ANTES de codificar) — dos decisiones de diseño de seguridad.**
> Resolver con `/bmad-party-mode` (Winston + Amelia + Murat) y documentar aquí antes de implementar:
>
> **(0.a) Topología de validación del token.** El AC-E6.1.1 dice "cuando llega al **Gateway** → 401", pero
> 6.2 (RBAC) y 6.3 (aislamiento) se resuelven **server-side en cada servicio**, que necesitan los claims.
> Opciones:
> - **(A) Gateway valida + servicios validan (defensa en profundidad, RECOMENDADA).** JwtBearer en el Gateway
>   (401 en el borde) **y** JwtBearer en cada `*.Api` (los servicios no confían en un header inyectado como
>   mecanismo de auth; YARP reenvía el `Authorization` header intacto). Coherente con "servicios no expuestos
>   directamente" + "RBAC server-side". Cierra el anti-patrón de confiar en `X-Agente`.
> - **(B) Solo el Gateway valida** y reenvía claims por headers firmados. Menos superficie de validación pero
>   los servicios quedan expuestos si alguien evita el Gateway; contradice "server-side".
>
> **(0.b) Emisor de tokens (no hay IdP externo en el alcance).** La arquitectura dice "JWT/OIDC **propio**".
> Opciones para que Postman/Newman/tests obtengan un token sin instalar un IdP:
> - **`dotnet user-jwts` (RECOMENDADA para dev)** — herramienta nativa: firma JWTs de dev con clave en
>   user-secrets, validados por JwtBearer sin código de emisión propio. Cero secretos en repo. Documentar en README.
> - **Endpoint de emisión mínimo** (`POST /auth/token` de dev que firma un JWT con rol/identidad) — útil para
>   Newman automatizado; marcar como **solo-dev** (no en prod) y sin credenciales hardcodeadas.
> - **OIDC real (Keycloak/Azure AD B2C)** — fuera del timebox de la prueba; se documenta como camino a producción.

- [ ] **Task 1 — Paquete y configuración de JwtBearer (AC: 1, 2)**
  - [ ] Añadir `Microsoft.AspNetCore.Authentication.JwtBearer` (versión .NET 10) a `Directory.Packages.props` (CPM).
  - [ ] Configuración compartida de validación (issuer/audience/lifetime/signing key) — extraer a un helper en
    `Comun.Web` para que Gateway y los 3 `*.Api` la reutilicen sin drift (`AddAutenticacionJwt(config)`).
  - [ ] `TokenValidationParameters`: `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`,
    `ValidateIssuerSigningKey` todos en `true`; `ClockSkew` acotado (≤ 5 min, explícito).
- [ ] **Task 2 — Wiring en el Gateway (AC: 1)** *(TDD: test de 401 primero)*
  - [ ] `AddAuthentication().AddJwtBearer(...)` + `UseAuthentication()`/`UseAuthorization()` en `ApiGateway/Program.cs`.
  - [ ] `RequireAuthorization()` global sobre las rutas de negocio de YARP; excluir `/health`, `/alive` y (si aplica) el endpoint de emisión de dev.
  - [ ] El `401` emite Problem Details (reusar el pipeline de `Comun.Web`), no la página HTML por defecto.
- [ ] **Task 3 — Wiring en cada servicio (AC: 1, 2, 4)** *(según Task 0.a; RECOMENDADO: A)*
  - [ ] `AddAutenticacionJwt` + `UseAuthentication/UseAuthorization` en `Hoteles.Api`, `Reservas.Api`,
    (y `Notificaciones.Worker` si expusiera HTTP — hoy no).
  - [ ] `[Authorize]`/`RequireAuthorization()` por defecto en los endpoints de negocio (6.2 refina por rol).
- [ ] **Task 4 — Emisor de tokens para dev/test (AC: 2, 3, 4)** *(según Task 0.b)*
  - [ ] Configurar el emisor elegido; claims mínimos: `sub`/email (identidad del agente), `role` (`Agente`/`Viajero`).
  - [ ] Documentar en README cómo obtener un token para Postman/Newman **sin** secretos en el repo.
- [ ] **Task 5 — Tests (AC: 1, 2, 4)**
  - [ ] Integración a nivel HTTP (`WebApplicationFactory`): sin token → `401`; token expirado → `401`; token con
    issuer/audience/firma inválidos → `401`; token válido → NO `401` (200/403/negocio según endpoint).
  - [ ] Unit: el helper `AddAutenticacionJwt` produce `TokenValidationParameters` con las 4 validaciones activas.
  - [ ] Los tests **generan** sus JWTs con la misma clave de dev (no hardcodean tokens capturados).
- [ ] **Task 6 — Commits en rama `feature/6-1-autenticacion-jwt-oidc` + PR a `develop`** (autor Santiago Renteria; sin trailers de IA; `dotnet format` antes de pushear).

## Dev Notes

### Estado actual que esta historia toca (leer antes de codificar)

- **`ApiGateway/Program.cs`** — YARP puro hoy; el comentario de línea 7 dice literalmente "La autenticación JWT,
  el rate limiting y HTTPS enforcement se añaden en la Épica 6". Aquí entra JwtBearer + `RequireAuthorization`.
  [Source: src/ApiGateway/Program.cs]
- **`Reservas.Api/Program.cs` y `Hoteles.Api/Program.cs`** — `AddServiceDefaults` + OpenAPI + mediator + endpoints
  minimal API. Hoy **ningún** endpoint exige autenticación. HTTPS enforcement está delegado al Gateway (los
  servicios corren HTTP tras él). [Source: src/Servicios/*/*.Api/Program.cs]
- **`Reservas.Api/HttpContextoAgente.cs`** — resuelve la identidad del agente desde la cabecera `X-Agente`.
  Está documentado como **puente explícito hasta la Épica 6**: "se sustituye por una lectura del claim del token
  — sin tocar handlers ni queries". Esta historia **no** elimina aún el header (lo hace 6.3), pero deja el token
  validado con el claim de identidad disponible para que 6.3 cambie solo la fuente de lectura. [Source: HttpContextoAgente.cs, IContextoAgente.cs]
- **`Comun.Web`** — hoy tiene `ManejadorExcepcionesNegocio` (Problem Details RFC 7807). El 401/403 deben salir
  como Problem Details, no como respuestas por defecto. Reusar/extender este proyecto para el wiring compartido. [Source: src/Comun/HotelBookingHub.Comun.Web/]

### Arquitectura (fuente `architecture.md` + `security-and-quality.md`)

- **JWT/OIDC propio + RBAC server-side** (Agente/Viajero), también en nube (ADR-006). 401 sin token, 403 sin
  permiso, aislamiento agente↔agente en autorización. [Source: architecture.md#Authentication-&-Security]
- **Cero secretos en repo:** Dapr Secrets (local) / Key Vault (nube); SAST + gitleaks en CI. Prohibido hardcodear
  secretos/credenciales/connection strings; `appsettings.json` solo valores no sensibles. [Source: security-and-quality.md]
- **Gateway = borde único** (`dotnet new web` + YARP): enruta/agrega, sin lógica de negocio; auth JWT + rate
  limiting + HTTPS viven aquí. Los servicios no se exponen directamente. [Source: architecture.md#API-&-Communication-Patterns]
- **Práctica OWASP #1 (A07):** AuthN JWT/OIDC con issuer/audience/expiración (+ refresh tokens si aplica).
  [Source: security-and-quality.md — tabla de 8 prácticas]
- **Mapeo excepción→HTTP:** sin token → **401**; sin permiso/agente ajeno → **403**. [Source: architecture.md#Implementation-Patterns]

### Latest tech (verificar al implementar)

- .NET 10 / ASP.NET Core: `Microsoft.AspNetCore.Authentication.JwtBearer` versión alineada con `net10.0`
  (fijar por CPM; el repo ya pin-ea versiones y bloquea CVEs — ver el pin de `Microsoft.OpenApi`).
- `dotnet user-jwts`: herramienta idiomática de dev para emitir/validar JWTs con clave en user-secrets, sin
  emisor propio ni secretos en repo. Verificar disponibilidad en .NET 10.
- YARP 2.3.0 (ya en CPM): reenvía el header `Authorization` por defecto; confirmar que no lo strippea.

### Anti-patrones a evitar

- Confiar en un header inyectado (`X-Agente`) **como autenticación** (no lo es; es identidad sin verificar).
- Clave de firma / secretos en `appsettings.json` o en el código (rompe AC-E6.1.3 + gitleaks).
- Deshabilitar la validación de issuer/audience/lifetime "para que pase el test".
- Devolver HTML de error por defecto en 401 (romper el contrato Problem Details del sistema).
- Validar solo en el Gateway y dejar los servicios abiertos si se resuelve la topología B sin compensación.

### Testing

- Nivel HTTP con `WebApplicationFactory` (cierra parte de la deuda transversal "sin test a nivel HTTP" registrada
  en `deferred-work.md`): 401 en sus tres variantes (ausente/expirado/firma inválida) + happy path.
- Los tests generan JWTs con la clave de dev (helper de test), nunca tokens hardcodeados.

### Project Structure Notes

- NUEVO helper compartido en `Comun.Web` (`AddAutenticacionJwt`/config de validación).
- MODIFICADOS: `ApiGateway/Program.cs`, `Hoteles.Api/Program.cs`, `Reservas.Api/Program.cs`, `Directory.Packages.props`.
- NUEVOS tests: `tests/*.IntegrationTests` (o un `FunctionalTests` transversal) para el 401/happy path.

### References

- [epics.md — Story 6.1 (AC-E6.1.1)](../planning-artifacts/epics.md)
- [architecture.md — Authentication & Security, API & Communication](../planning-artifacts/architecture.md)
- [security-and-quality.md — 8 prácticas OWASP (#1 AuthN, #5 secretos)](../specs/spec-hotel-booking-hub/security-and-quality.md)
- [HttpContextoAgente.cs / IContextoAgente.cs](../../src/Servicios/Reservas/Reservas.Api/HttpContextoAgente.cs) — puente X-Agente a cerrar en 6.3
- [deferred-work.md — IDOR + Iniciador autodeclarado (→ Épica 6)](deferred-work.md)

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List

### Change Log
