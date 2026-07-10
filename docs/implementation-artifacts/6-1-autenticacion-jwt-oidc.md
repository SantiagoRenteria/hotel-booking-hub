# Story 6.1: Autenticación JWT/OIDC

---
baseline_commit: 9191d80ba2f693791a427c8cdabccfde332f919c
---

Status: done

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

> **✅ Task 0 RESUELTA (criterio de ingeniería, modo autónomo — NO ameritó party-mode) — 2026-07-10.**
> Ambas decisiones tenían una opción claramente correcta de mejores prácticas, resoluble sin bloqueo (a
> diferencia del modelo de propiedad de Hoteles de 6.3, que sí exige a Santiago). Decisiones tomadas:
> - **(0.a) Topología → Opción A (defensa en profundidad).** JwtBearer valida en el **Gateway** (401 en el
>   borde, AC-E6.1.1) **y** en cada `*.Api` (los servicios no confían en un header inyectado como auth). YARP
>   reenvía `Authorization` intacto. Coherente con "RBAC server-side" + "servicios no expuestos directamente".
> - **(0.b) Emisor → clave simétrica compartida por config + emisión de dev documentada.** `dotnet user-jwts`
>   no comparte limpiamente una clave simétrica entre 3 apps con sección `Jwt` propia, así que la clave
>   (`Jwt:SigningKey`) se provee por env/user-secrets/Key Vault (cero secretos en repo) y los tokens de dev se
>   minan con esa clave (helper de referencia `JwtTestFactory.EmitirToken`; *pre-request script* para Postman).
>
> <details><summary>Contexto original de la decisión (opciones evaluadas)</summary>
>
> **Task 0 (party-mode ANTES de codificar) — dos decisiones de diseño de seguridad.**
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
>
> </details>

- [x] **Task 1 — Paquete y configuración de JwtBearer (AC: 1, 2)**
  - [x] Añadir `Microsoft.AspNetCore.Authentication.JwtBearer` (10.0.9) a `Directory.Packages.props` (CPM) + `Mvc.Testing` para el test funcional.
  - [x] Helper compartido `AddAutenticacionJwt`/`ConstruirParametrosValidacion` + `OpcionesJwt` en `Comun.Web/Seguridad/` (reutilizado por Gateway y `*.Api`, sin drift).
  - [x] `TokenValidationParameters`: `ValidateIssuer/Audience/Lifetime/IssuerSigningKey` en `true`; `ClockSkew` = 1 min. **Unit test RED→GREEN**.
- [x] **Task 2 — Wiring en el Gateway (AC: 1)** *(TDD: test de 401 primero — RED→GREEN)*
  - [x] `AddAutenticacionJwt` + `UseAuthentication()`/`UseAuthorization()` en `ApiGateway/Program.cs`.
  - [x] `MapReverseProxy().RequireAuthorization()`; `/health` y `/alive` quedan anónimos.
  - [x] El `401` emite Problem Details RFC 7807 (`OnChallenge`), no la respuesta por defecto.
- [x] **Task 3 — Wiring en cada servicio (AC: 1, 2, 4)** *(Task 0.a = A, defensa en profundidad)*
  - [x] `AddAutenticacionJwt` + `UseAuthentication/UseAuthorization` en `Hoteles.Api` y `Reservas.Api` (`Notificaciones.Worker` no expone HTTP).
  - [x] `RequireAuthorization()` en todos los endpoints de negocio (6.2 refina por rol). Health/alive anónimos.
- [x] **Task 4 — Emisor de tokens para dev/test (AC: 2, 3, 4)** *(Task 0.b)*
  - [x] Claims mínimos `sub`/email + `role` (`Agente`/`Viajero`) en el helper de emisión (`JwtTestFactory.EmitirToken`).
  - [x] Documentado en README: clave por env/user-secrets/Key Vault (cero secretos), token de dev para Postman; `docker-compose` + `.env.example` con `JWT_SIGNING_KEY`.
- [x] **Task 5 — Tests (AC: 1, 2, 4)**
  - [x] Funcional HTTP (`WebApplicationFactory` sobre el Gateway, sin DB): sin token/expirado/firma/issuer/audience → `401`; válido → NO `401` (6 tests).
  - [x] Unit: el helper produce `TokenValidationParameters` con las 4 validaciones activas + clock skew acotado + fail-closed sin clave (3 tests).
  - [x] Los tests **generan** sus JWTs con la clave de test (no hardcodean tokens capturados).
  - [x] Nota: el 401 a nivel de servicio (defensa en profundidad) se ejercitará con los tests HTTP de rol/aislamiento de 6.2/6.3 (evita colisión de dos `Program` públicos en un mismo proyecto de test).
- [x] **Task 6 — Commits en rama `feature/6-1-autenticacion-jwt-oidc` + PR a `develop`** (autor Santiago Renteria; sin trailers; `dotnet format` verde). PR [#17](https://github.com/SantiagoRenteria/hotel-booking-hub/pull/17).

### Review Findings

_Code review 2026-07-10 (3 capas: Blind Hunter + Edge Case Hunter + Acceptance Auditor). **Veredicto: CUMPLE los 4 AC**, sin secretos ni bypass de autenticación. 8 patch · 3 defer · 0 dismiss. **Los 8 patch se aplicaron vía `/bmad-agent-dev` (Amelia) — suite 406 tests en verde, `dotnet format` limpio.**_

> **Nota (patch 8):** el fix literal sugerido (cambiar a `ConfigureAppConfiguration`) era **incompatible** con el patch 1 (validación eager): esa fuente se aplica después de que Program.cs corre `AddAutenticacionJwt`, dejando la clave ausente al arrancar. Se resolvió el fondo del hallazgo (precedencia determinista) manteniendo `ConfigureHostConfiguration` (disponible al arranque y con precedencia en el host de test) con comentario explicativo.
> **Consecuencia del patch 1 (fail-fast):** los servicios ahora NO arrancan sin `JWT_SIGNING_KEY`; se añadió la generación de una clave efímera al smoke test de CI (`.github/workflows/ci.yml`) — cero secretos en repo.

- [x] **[Review][Patch] Validación fail-closed perezosa → 500 opaco en el borde** (MEDIA, `blind+edge+auditor`) — el `throw` de `ConstruirParametrosValidacion` vive dentro del lambda de `AddJwtBearer` (perezoso, por request). Con `${JWT_SIGNING_KEY:-}` vacío, los contenedores arrancan sanos (health anónimo) pero la primera petición protegida lanza `InvalidOperationException`; el Gateway no tiene `UseExceptionHandler` → 500 con cuerpo vacío. Fix: validar la config *eager* en `AddAutenticacionJwt` (fail-fast al arrancar). `[AutenticacionJwtExtensions.cs, ApiGateway/Program.cs]`
- [x] **[Review][Patch] Sin validación de longitud mínima de la clave (≥32 bytes / 256 bits)** (MEDIA, `blind+edge`) — el guard solo hace `IsNullOrWhiteSpace`; una clave corta se acepta pese a que `.env.example` promete ≥256 bits. Fix: rechazar `Encoding.UTF8.GetByteCount(SigningKey) < 32`. `[AutenticacionJwtExtensions.cs]`
- [x] **[Review][Patch] Issuer/Audience vacíos no validados → rechazo total opaco** (BAJA, `edge`) — con `ValidIssuer=""`/`ValidAudience=""` se rechaza todo token, indistinguible de "todos inválidos". Fix: validar no-vacíos junto a la clave. `[AutenticacionJwtExtensions.cs]`
- [x] **[Review][Patch] `ValidAlgorithms` no fijado** (BAJA, `edge`) — hoy seguro (clave simétrica + `RequireSignedTokens`), pero sin allow-list explícita reaparece la confusión HS/RS si se introduce clave asimétrica. Fix: `ValidAlgorithms = [SecurityAlgorithms.HmacSha256]`. `[AutenticacionJwtExtensions.cs]`
- [x] **[Review][Patch] `OnChallenge` sin guard `Response.HasStarted`** (BAJA, `edge`) — asignar StatusCode/headers tras iniciar la respuesta lanzaría. Fix: `if (respuesta.HasStarted) return;`. `[AutenticacionJwtExtensions.cs]`
- [x] **[Review][Patch] 401 Problem Details diverge del contrato del sistema** (BAJA, `blind+edge+auditor`) — objeto anónimo sin `traceId`/`instance`, `type` = rfc7235 (reto de auth) en vez del contrato uniforme. Fix: añadir `traceId` (de `Activity.Current`) + `instance` + `status`. `[AutenticacionJwtExtensions.cs]`
- [x] **[Review][Patch] Tests débiles no guardan la intención** (BAJA, `blind+auditor`) — clock skew asserta `<=5min` (no protege el endurecimiento a 1 min); la aserción positiva solo comprueba `!=401`; no se verifica `Content-Type: application/problem+json` ni `WWW-Authenticate: Bearer`. Fix: clock skew `==1min`; positivo asserta 5xx de proxy; añadir asserts del contrato de error. `[AutenticacionJwtTests.cs, AutenticacionGatewayTests.cs]`
- [x] **[Review][Patch] Fragilidad de precedencia de config en el test factory** (BAJA, `blind`) — el factory inyecta issuer/audience de test por `ConfigureHostConfiguration`, que difiere de `appsettings.json`; la suite depende de que host-config gane. Fix: usar `ConfigureAppConfiguration` (precedencia determinista sobre appsettings). `[JwtTestFactory.cs]`
- [x] **[Review][Defer] Clave HMAC simétrica compartida por Gateway + servicios** (MEDIA-diseño, `blind`) — cualquier componente puede forjar tokens válidos en todo el sistema; la defensa en profundidad se debilita si se filtra la clave. Riesgo aceptado en el alcance de la prueba (JWT/OIDC propio, sin IdP). Producción: claves asimétricas (RS256, clave privada de firma aislada del validador). Diferido — documentado en `deferred-work.md`.
- [x] **[Review][Defer] Autorización opt-in en servicios vs secure-by-default** (BAJA-MEDIA, `blind`) → **Story 6.2** — un endpoint futuro sin `.RequireAuthorization()` quedaría anónimo (servicios expuestos en 8081/8082). 6.2 consolida el modelo de autorización por endpoint (roles) y adoptará `FallbackPolicy` con `AllowAnonymous` explícito en health. Diferido a 6.2.
- [x] **[Review][Defer] Naming del claim de rol (`ClaimTypes.Role` URI vs `role`)** (INFO, `auditor`) → **Story 6.2** — el emisor usa `ClaimTypes.Role` (URI larga); el README documenta `role`. Funciona con `IsInRole` (RoleClaimType por defecto), pero una policy que espere `role` corto tendría fricción. 6.2 (dueña del RBAC) fija el `RoleClaimType`/claim. Diferido a 6.2.

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

claude-opus-4-8 (bmad-dev-story, modo autónomo).

### Debug Log References

- Ciclo TDD visible en commits: `test(6.1)` rojo → `feat(6.1)` verde, dos veces (helper unit + funcional del Gateway).
- Suite completa tras el wiring: **401 tests en verde** — Comun.Web 18, Contracts 22, Notif.Unit 29, Hoteles.Unit 100, Reservas.Unit 153, Seguridad.Functional 6, Notif.Int 6, Hoteles.Int 19, Reservas.Int 48. `dotnet format --verify-no-changes` limpio. Build 0 warnings (`TreatWarningsAsErrors`).
- Fix durante el rojo funcional: el helper de emisión ponía `notBefore` fijo posterior al `expires` de un token expirado → `JwtSecurityToken` lanzaba IDX12401; se ató `notBefore` a `expires - 2h`.

### Completion Notes List

- **Task 0 (criterio de ingeniería, sin party-mode):** topología A (JwtBearer en Gateway + servicios, defensa en profundidad); clave simétrica compartida por config (env/user-secrets/Key Vault), tokens de dev minados con esa clave. No fue un bloqueo Santiago-level (a diferencia del modelo de propiedad de Hoteles de 6.3).
- **AC-E6.1.1** (401 en el borde): 6 tests funcionales verdes — sin token / expirado / firma inválida / issuer / audience erróneos → 401 Problem Details; válido → != 401.
- **AC-E6.1.2** (token válido pasa): verificado (no rechazado por auth).
- **AC-E6.1.3** (cero secretos): clave nunca en el repo; `appsettings.json` solo `Issuer`/`Audience`; `docker-compose` toma la clave de `JWT_SIGNING_KEY` (`.env`, gitignored). El health anónimo mantiene el smoke test verde aun sin clave.
- **AC-E6.1.4** (claims): el token porta `sub`/`email` + `role`; JwtBearer los expone en `HttpContext.User` — base de 6.2 (RBAC) y 6.3 (aislamiento).
- **Deuda que 6.1 deja lista para 6.3:** la costura `IContextoAgente` sigue leyendo `X-Agente`; 6.3 cambia la fuente al claim del token ya validado aquí.

### File List

**Producción (nuevos):**
- `src/Comun/HotelBookingHub.Comun.Web/Seguridad/AutenticacionJwtExtensions.cs`

**Producción (modificados):**
- `Directory.Packages.props` (JwtBearer 10.0.9 + Mvc.Testing 10.0.9)
- `src/Comun/HotelBookingHub.Comun.Web/HotelBookingHub.Comun.Web.csproj` (PackageReference JwtBearer)
- `src/ApiGateway/ApiGateway.csproj` (ref a Comun.Web), `src/ApiGateway/Program.cs` (auth + RequireAuthorization + `public partial class Program`), `src/ApiGateway/appsettings.json` (sección Jwt)
- `src/Servicios/Hoteles/Hoteles.Api/Program.cs` + `appsettings.json` (auth + RequireAuthorization ×9)
- `src/Servicios/Reservas/Reservas.Api/Program.cs` + `appsettings.json` (auth + RequireAuthorization ×8)
- `deploy/docker-compose.yml` (`Jwt__SigningKey` en gateway/hoteles/reservas), `deploy/.env.example` (`JWT_SIGNING_KEY`)
- `README.md` (sección de autenticación JWT)
- `.github/workflows/ci.yml` (smoke test genera `JWT_SIGNING_KEY` efímero — consecuencia del fail-fast del review)

**Tests (nuevos):**
- `tests/Comun.Web.UnitTests/AutenticacionJwtTests.cs`
- `tests/Seguridad.FunctionalTests/` (proyecto nuevo: `.csproj`, `JwtTestFactory.cs`, `AutenticacionGatewayTests.cs`) + alta en `HotelBookingHub.slnx`

### Change Log

- 2026-07-10 — Story 6.1 implementada. JWT/OIDC: helper compartido de validación (Comun.Web), wiring en Gateway (401 en el borde, AC-E6.1.1) + servicios (defensa en profundidad). Cero secretos en repo (clave por env/user-secrets). Claims de identidad/rol disponibles para 6.2/6.3. TDD Red→Green visible. Suite completa 401 tests en verde.
- 2026-07-10 — Code review (3 capas): CUMPLE los 4 AC. 8 patch aplicados vía agent-dev (fail-fast de config eager, longitud/no-vacío de clave/issuer/audience, `ValidAlgorithms=HS256`, guard `HasStarted`, `traceId`/`instance` en el 401, tests endurecidos) + smoke de CI genera `JWT_SIGNING_KEY` efímero. 3 defer (clave simétrica compartida, secure-by-default y naming del claim de rol → 6.2). Suite 406 tests en verde, `dotnet format` limpio.
