# Seguridad

> Este doc responde: qué prácticas de seguridad aplica el sistema y por qué → OWASP.

`hotel-booking-hub` es un sistema de microservicios (.NET 10): dos bounded contexts (**Hoteles** y **Reservas**), un **API Gateway** (YARP) como único borde de entrada y un **Worker de Notificaciones**. La seguridad se diseñó en profundidad (borde + cada servicio) y se referencia contra el [OWASP Top 10:2021](https://owasp.org/Top10/). Cada práctica indica el **porqué** y **dónde vive** en el código.

## Tabla resumen

| # | Práctica | OWASP | Dónde en el código | Por qué |
|---|----------|-------|--------------------|---------|
| 1 | Autenticación JWT validada en el borde **y** en cada servicio (defensa en profundidad) | A07 — Identification & Authentication Failures | `AutenticacionJwtExtensions.cs`; `ApiGateway/Program.cs:23,114`; `Hoteles.Api/Program.cs:28`; `Reservas.Api/Program.cs:37` | Un servicio nunca confía en que "alguien antes" autenticó; valida issuer/audience/expiración/firma él mismo |
| 2 | Autorización RBAC por rol (`SoloAgente` / `AgenteOViajero`) server-side | A01 — Broken Access Control | `AutorizacionPorRolExtensions.cs`; `RequireAuthorization(...)` en cada endpoint | El permiso lo decide el servidor por rol del token, no el cliente ni el gateway |
| 3 | Aislamiento entre agentes / anti-IDOR (identidad server-side, 404 en recurso ajeno) | A01 — Broken Access Control | `ClaimContextoAgente.cs`; `HotelesDbContext.cs:46` (query filter global) | La identidad sale del claim `email` validado, no de un header confiable; el recurso ajeno es invisible → 404, no 403 |
| 4 | Cero secretos en el repo (`random_password` + Key Vault + Managed Identity passwordless; gitleaks en CI; `.env` gitignored) | A02 — Cryptographic Failures / A05 — Security Misconfiguration | `deploy/terraform/keyvault.tf`, `main.tf:22,29`; `.github/workflows/ci.yml:61`; `.gitignore:42-43` | Ningún secreto vive en el árbol de fuentes; los genera Terraform, los custodia Key Vault y los leen las apps sin contraseña (OIDC/MI) |
| 5 | Validación de entrada estricta (FluentValidation, regex con matchTimeout, límites de longitud) | A03 — Injection / validación de entrada | `CrearReservaCommandValidator.cs`; `CrearHotelCommandValidator.cs`; `ExpresionesValidacion.cs` | Toda entrada mal formada se corta con 400 antes del handler; regex lineal con timeout anti-ReDoS |
| 6 | Rate limiting en el gateway (sliding window por IP real) | A04 — Insecure Design (protección DoS/abuso) | `ApiGateway/Program.cs:34-72` | Acota fuerza bruta y abuso en el único borde; exceso → 429 con `Retry-After` |
| 7 | Security headers + HTTPS enforcement (HSTS) en el gateway (borde único) | A05 — Security Misconfiguration | `ApiGateway/Program.cs:74-104` (CORS allowlist, HSTS, ForwardedHeaders) | El borde impone HTTPS/HSTS y una allowlist CORS explícita; nunca `AllowAnyOrigin` |
| 8 | Problem Details RFC 7807 sin filtrar internals + concurrencia optimista (409) | A04 — Insecure Design (fuga de información) | `ManejadorExcepcionesNegocio.cs`; `ResultadoHttpExtensions.cs:20-28` | El error de negocio expone un contrato uniforme; un bug real cae al 500 genérico sin filtrar stack ni SQL |

---

## 1. Autenticación JWT en el borde y en cada servicio (A07)

**Qué.** El token JWT se valida con las cuatro comprobaciones activas (issuer, audience, lifetime, firma HMAC-SHA256) tanto en el gateway como dentro de cada `*.Api`.

**Por qué.** Defensa en profundidad: si un atacante alcanzara un servicio saltándose el gateway (red interna comprometida, misconfig del ingress), el servicio sigue exigiendo un token válido. No se confía en un header inyectado por un salto previo.

**Dónde.**
- Helper compartido `src/Comun/HotelBookingHub.Comun.Web/Seguridad/AutenticacionJwtExtensions.cs` — validación idéntica sin drift: `ValidateIssuer/Audience/Lifetime/IssuerSigningKey`, algoritmo fijado a `HmacSha256` (allow-list explícita, anti algorithm-confusion), `ClockSkew` acotado a 1 min y validación **fail-closed** de la config al arrancar (clave ≥ 256 bits, issuer/audience presentes).
- Borde: `src/ApiGateway/Program.cs:23` (registro) y `:114` (`MapReverseProxy().RequireAuthorization()`).
- Cada servicio: `src/Servicios/Hoteles/Hoteles.Api/Program.cs:28` y `src/Servicios/Reservas/Reservas.Api/Program.cs:37`.
- El `401` se emite como Problem Details RFC 7807 (cuerpo uniforme), `AutenticacionJwtExtensions.cs:116-125`.

## 2. Autorización RBAC por rol server-side (A01)

**Qué.** Dos policies —`SoloAgente` (gestión de catálogo, resolución de cancelaciones, listados del agente) y `AgenteOViajero` (búsqueda, crear reserva, solicitar cancelación)— resueltas por .NET Authorization.

**Por qué.** El control de acceso es una decisión de servidor. Un rol sin permiso recibe `403` sin que el cliente pueda alterarlo.

**Dónde.**
- `src/Comun/HotelBookingHub.Comun.Web/Seguridad/AutorizacionPorRolExtensions.cs:37-41` (`RequireRole`), roles canónicos en `RolesAplicacion` (un solo lugar, sin drift de strings).
- Aplicación por endpoint: `.RequireAuthorization(PoliticasAutorizacion.SoloAgente)` / `AgenteOViajero` en `Hoteles.Api/Program.cs` (todo el CRUD de catálogo es `SoloAgente`) y `Reservas.Api/Program.cs:124,152,166,176,198`.
- El `403` reutiliza el mismo Problem Details que el `401` (`AutenticacionJwtExtensions.cs:128-134`).

## 3. Aislamiento entre agentes / anti-IDOR (A01)

**Qué.** Un agente solo ve y muta sus propios recursos. La identidad se resuelve **server-side** desde el claim `email` del token validado; un recurso de otro agente es **invisible** y devuelve `404` (no `403`, para no revelar su existencia).

**Por qué.** Evita IDOR: el cliente no elige a qué agente ve ni confía en un header (`X-Agente` de la Story 3.3 quedó cerrado). El aislamiento es **fail-closed**: sin identidad no casa ningún registro.

**Dónde.**
- `src/Servicios/Hoteles/Hoteles.Api/ClaimContextoAgente.cs` (y su gemelo en Reservas): lee `email`, normaliza (trim + minúsculas), y ante ausencia o **ambigüedad** (varios claims `email`) → `null` (no adivina).
- Query filter global por propietario: `src/Servicios/Hoteles/Hoteles.Infrastructure/Persistencia/HotelesDbContext.cs:46` — `HasQueryFilter(h => !h.Eliminado && (!_aislamientoActivo || h.AgentePropietario == _agenteActual))`. "Un solo lugar decide": toda carga-para-mutar de un hotel ajeno devuelve 404 sin guard por handler. El bypass solo aplica sin costura de identidad (migraciones/design-time/siembra de tests).
- `AgentePropietario` es `NOT NULL` (greenfield), por lo que una identidad ausente nunca casa (`== null` → 0 filas).
- Reservas resuelve lo mismo vía `IContextoAgente` en sus queries/handlers (`Reservas.Api/Program.cs:72-73`).

## 4. Cero secretos en el repositorio (A02 / A05)

**Qué.** Ningún secreto (contraseña SQL, clave JWT, cadenas de Service Bus/Redis) vive en el árbol de fuentes.

**Por qué.** Un secreto commiteado es una fuga permanente en el historial de git. Se generan en despliegue, se custodian en Key Vault y las apps los leen **sin contraseña** (Managed Identity / OIDC federated).

**Dónde.**
- Generación: `deploy/terraform/main.tf:22` (`random_password "sql_admin"`) y `:29` (`random_password "jwt"`).
- Custodia: `deploy/terraform/keyvault.tf` — Key Vault con RBAC (no access policies); la Managed Identity de las apps tiene rol `Key Vault Secrets User` (passwordless, ADR-020); purge protection en prod.
- Barrera en CI: `.github/workflows/ci.yml:61-73` — job `gitleaks` (escaneo de secretos con `.gitleaks.toml`).
- `.gitignore:42-43` — `.env` y `.env.*` ignorados (solo se versiona `deploy/.env.example`). En CI los secretos del smoke de compose se generan efímeros con `openssl rand` (`ci.yml:86-91`).
- ADRs relacionados: `docs/adr/ADR-006-jwt-propio-oidc-rbac-tambi-n-en-nube.md`, `docs/adr/ADR-020-gesti-n-de-secretos-por-entorno-env-vars-local-dapr-secrets.md`, `docs/adr/ADR-021-...` (CD por OIDC federated).

## 5. Validación de entrada estricta (A03 / validación)

**Qué.** Todo comando/query pasa por FluentValidation antes del handler; los campos obligatorios, formatos (email, teléfono, documento) y límites de longitud se verifican y un fallo devuelve `400`.

**Por qué.** Reduce superficie de inyección y datos malformados que reventarían más abajo (p. ej. un valor sobredimensionado que truncaría en el INSERT → 500). Las regex son lineales y con `matchTimeout` explícito (anti-ReDoS).

**Dónde.**
- Reservas: `src/Servicios/Reservas/Reservas.Application/Reservas/CrearReserva/CrearReservaCommandValidator.cs` — cada huésped y el contacto de emergencia obligatorios, email `EmailAddress()`, tope de estancia (365 noches), enums por nombre con `IsInEnum`.
- Regex con timeout: `src/Servicios/Reservas/Reservas.Application/Reservas/CrearReserva/ExpresionesValidacion.cs:12,16` — teléfono `^\+?\d{7,15}$` y documento `^[A-Za-z0-9\-]{4,20}$`, ambos con `matchTimeoutMilliseconds: 200`.
- Hoteles: `src/Servicios/Hoteles/Hoteles.Application/Hoteles/CrearHotel/CrearHotelCommandValidator.cs` — nombre/ciudad obligatorios y longitudes desde la misma fuente que el mapeo EF (`LongitudesHotel`).
- El corte a `400` lo hace el `ValidationBehavior` del pipeline del mediator (`AddMediatorPipeline`), homogéneo entre servicios.

## 6. Rate limiting en el gateway (A04 — protección DoS/abuso)

**Qué.** Sliding window por cliente (IP real del cliente tras `ForwardedHeaders`) en el único borde; el exceso devuelve `429`.

**Por qué.** Acota fuerza bruta contra login/búsqueda y protege el sistema de abuso. Configurable por entorno (`RateLimit:PermitLimit` / `WindowSeconds`, validados > 0 al arrancar).

**Dónde.**
- `src/ApiGateway/Program.cs:34-72` — `AddRateLimiter` con `SlidingWindowRateLimiter` particionado por IP; `/health` y `/alive` exentos (las sondas del orquestador no deben recibir 429).
- El `429` emite Problem Details RFC 7807 + `Retry-After` (`Program.cs:38-52`), coherente con el 401/403.
- La IP real se restaura con `UseForwardedHeaders()` (`Program.cs:15-20,94`) para particionar correctamente detrás del ingress.

## 7. Security headers + HTTPS enforcement en el borde único (A05)

**Qué.** El gateway impone HSTS, CORS con allowlist explícita y confía la terminación TLS al ingress.

**Por qué.** Concentrar el endurecimiento de transporte en un único borde evita configuraciones divergentes por servicio. CORS nunca usa `AllowAnyOrigin`; la allowlist arranca **vacía** (ningún origen cross-site) y se puebla por entorno.

**Dónde.**
- `src/ApiGateway/Program.cs:74-78` — CORS `WithOrigins(origenes)` desde `Cors:AllowedOrigins`, allowlist vacía por defecto.
- `src/ApiGateway/Program.cs:80-85,98-101` — HSTS con `IncludeSubDomains` y `max-age` de 1 año; `UseHsts()` fuera de Development.
- La terminación TLS y la redirección viven en el ingress (ACA/Azure, ADR-008); los servicios internos corren HTTP intencionalmente tras el borde (`Hoteles.Api/Program.cs:74`, `Reservas.Api/Program.cs:103`).

## 8. Problem Details RFC 7807 sin filtrar internals + concurrencia optimista (A04)

**Qué.** Todos los errores exponen un contrato uniforme RFC 7807. Los conflictos de concurrencia optimista devuelven `409`; un bug real cae al `500` genérico sin filtrar stack, SQL ni detalles internos.

**Por qué.** Evita fuga de información en mensajes de error y da un contrato de errores predecible. La concurrencia optimista (`rowVersion`) protege contra pérdidas de actualización sin exponer detalles del motor.

**Dónde.**
- `src/Comun/HotelBookingHub.Comun.Web/ManejadorExcepcionesNegocio.cs:22-29` — solo las `ExcepcionNegocio` se traducen a Problem Details; **cualquier otra excepción** hace `return false` y cae al 500 por defecto (un bug nunca se enmascara como respuesta de negocio).
- `src/Comun/HotelBookingHub.Comun.Web/ResultadoHttpExtensions.cs:20-28` — única tabla `EstadoResultado → HTTP` compartida por el mapeo `Result` y el handler de excepciones: el mismo `Conflicto` produce `409` venga por `Result` o por excepción.
- Concurrencia optimista con `rowVersion` en los endpoints de mutación (`Hoteles.Api/Program.cs:92-131`, `409` si pierde la carrera) y `ConflictoConcurrenciaException` (`src/Comun/HotelBookingHub.Comun/Excepciones/ConflictoConcurrenciaException.cs`). Overbooking en Reservas → `409` por la misma vía.

---

Cero-secretos verificado por **gitleaks en CI** (`.github/workflows/ci.yml:61-73`).
