# Story 6.4: Endurecimiento OWASP (8 prácticas)

Status: ready-for-dev

<!-- Generado por bmad-create-story (modo autónomo, Épica 6). Complejidad NORMAL-ALTA en superficie pero
mayormente CONFIGURACIÓN + DOCUMENTACIÓN (varias prácticas ya existen del trabajo previo). Diferenciador ·
Fase 2. Cierra la épica. Alcance acotado (party-mode Winston): se DOCUMENTA el mapeo completo a OWASP Top 10;
se EJERCITA con código el subconjunto aplicable. No perseguir un barrido exhaustivo del Top 10 (gold-plating). -->

## Story

Como **responsable de seguridad**,
quiero **8 prácticas mapeadas a OWASP Top 10 (2021) implementadas/documentadas y cero secretos en el repositorio**,
para **demostrar seguridad proporcional y verificable**.

## Acceptance Criteria

1. **AC-E6.4.1 — Prácticas implementadas (acotadas a lo aplicable).** Dado el sistema desplegado, cuando se
   audita, entonces están **activas y documentadas** las prácticas aplicables al alcance: **rate limiting**,
   **validación/anti-inyección** (FluentValidation + EF parametrizado), **manejo de secretos** (user-secrets/Dapr
   Secrets / Key Vault), **HTTPS/HSTS + CORS allowlist**, **logging de eventos de seguridad sin PII**, **protección
   de PII**. El mapeo completo a OWASP Top 10 queda documentado.
2. **AC-E6.4.2 — Cero secretos en el repo (CI).** Dado un push, cuando corre gitleaks/SAST en CI, entonces `0`
   hallazgos de secretos y `0` hallazgos críticos.

## Tasks / Subtasks

> **Task 0 (alcance — party-mode Winston ya decidido en epics.md).** DOCUMENTAR el mapeo completo de las 8
> prácticas a OWASP Top 10; EJERCITAR con código el subconjunto aplicable (authz/aislamiento ya en 6.2/6.3,
> parametrización EF, datos sensibles/PII, secretos, rate limiting, HTTPS/HSTS/CORS, logging). Un barrido
> exhaustivo del Top 10 es gold-plating para la prueba. [Source: epics.md — nota de alcance de AC-E6.4.1]

- [ ] **Task 1 — Rate limiting en el Gateway (práctica #3, A07/A04)**
  - [ ] `AddRateLimiter` (sliding window) en `ApiGateway/Program.cs`; proteger login/emisión de token y búsqueda.
  - [ ] Exceso → `429` (Problem Details); documentar límites elegidos. Test de que se dispara el 429.
- [ ] **Task 2 — HTTPS/HSTS + CORS allowlist (práctica #6, A05)**
  - [ ] HSTS + redirección HTTPS en el Gateway (borde único; HTTP solo en localhost). CORS con **lista explícita**,
    nunca `AllowAnyOrigin()`. Documentar la allowlist por entorno.
- [ ] **Task 3 — Logging de eventos de seguridad sin PII (práctica #7, A09)**
  - [ ] Log estructurado de: login ok/fallido, 401/403, rate-limit disparado, cambios sensibles. **Sin PII ni
    secretos** en los logs (verificar que email/documento/token no se loguean). Enriquecido con `trace-id`.
- [ ] **Task 4 — Protección de PII + integridad (práctica #8, A02/A08/A10)**
  - [ ] Revisar exposición de PII de huéspedes (documento, email, teléfono, fecha de nacimiento): no en logs,
    minimizada en respuestas. Evaluar cifrado de columnas sensibles (o documentar decisión de alcance).
  - [ ] Toda `Regex` con `matchTimeout` explícito (anti-ReDoS, 100 ms–2 s) — auditar validators existentes.
  - [ ] Dependency scanning (ya hay pin de CVE en `Microsoft.OpenApi`); confirmar que CI falla ante CVE crítico.
- [ ] **Task 5 — Anti-inyección / validación (práctica #4, A03) — verificación, ya implementada**
  - [ ] Confirmar FluentValidation en todos los comandos y EF Core parametrizado (sin SQL dinámico). Documentar.
- [ ] **Task 6 — Manejo de secretos (práctica #5, A02) — verificación + doc**
  - [ ] Confirmar cero secretos hardcodeados; `appsettings.json` solo placeholders/no sensibles; user-secrets/Dapr
    en dev, Key Vault en nube (documentar el mecanismo por entorno).
- [ ] **Task 7 — Documento de seguridad (entregable del enunciado)**
  - [ ] Actualizar/crear el documento de seguridad con la tabla de 8 prácticas → OWASP Top 10, qué se ejercita con
    código vs qué se documenta, y evidencia (tests/CI). Alinear con `security-and-quality.md`.
- [ ] **Task 8 — Verificar gate de CI (AC: 2)**
  - [ ] gitleaks + SAST en verde con `0` secretos y `0` críticos; añadir cualquier práctica testeable al pipeline.
- [ ] **Task 9 — Commits en rama `feature/6-4-endurecimiento-owasp` + PR a `develop`** (autor Santiago Renteria; sin trailers; `dotnet format`).

## Dev Notes

### Qué ya existe (verificar, no reinventar)

- **#2 AuthZ/RBAC** → 6.2. **Aislamiento (A01)** → 6.3. **#1 AuthN JWT** → 6.1. Esta historia NO los rehace;
  los referencia como evidencia de A01/A07.
- **#4 Anti-inyección:** FluentValidation ya en todos los comandos (`ValidationBehavior`); EF Core parametrizado
  (sin SQL dinámico). Mayormente verificación + doc. [Source: security-and-quality.md, Comun/.../ValidationBehavior.cs]
- **gitleaks/SAST en CI** ya presente desde Story 1.1 (AC-E1.1.3). Pin de CVE en `Microsoft.OpenApi` 2.10.0 ya
  hecho en CPM. [Source: Directory.Packages.props, .github/workflows/ci.yml]
- **Diseño seguro (A04):** el invariante anti-overbooking (E1) y la idempotencia (E3/E5) son controles de diseño,
  no parches — citar como evidencia de A04. [Source: security-and-quality.md]

### Estado actual que esta historia toca

- **`ApiGateway/Program.cs`** — rate limiting + HTTPS/HSTS + CORS entran aquí (borde único). El comentario de la
  línea 7 los anticipa junto con la auth de 6.1. [Source: src/ApiGateway/Program.cs]
- HTTPS enforcement es responsabilidad del Gateway; los servicios corren HTTP tras él (no duplicar). [Source: *.Api/Program.cs]

### Arquitectura (fuente `security-and-quality.md`)

- Tabla de 8 prácticas → OWASP Top 10 (2021): #1 AuthN (A07), #2 AuthZ/RBAC (A01), #3 rate limiting (A07/A04),
  #4 validación/anti-inyección (A03), #5 secretos (A02), #6 HTTPS/HSTS/CORS (A05), #7 logging seguridad (A09),
  #8 PII+integridad (A02/A08/A10 SSRF). [Source: security-and-quality.md — tabla]
- Reglas de código no negociables: sin secretos hardcodeados; `Regex` con `matchTimeout`; HTTP solo localhost;
  CORS con lista explícita; secreto commiteado → comprometido y rotar. [Source: security-and-quality.md#Reglas]
- Cumplimiento mapeado: PCI DSS readiness, ISO 27001 (A.9/A.10/A.12) — documentar, no implementar pagos. [Source: security-and-quality.md#Cumplimiento]

### Anti-patrones a evitar

- Perseguir el Top 10 completo con código (gold-plating; el alcance acotado es decisión party-mode explícita).
- `AllowAnyOrigin()`/`AllowAnyHeader()` en CORS de producción.
- Loguear PII (email, documento, teléfono, fecha de nacimiento) o secretos/tokens.
- Duplicar HTTPS enforcement en los servicios (es del Gateway).
- Documentar una práctica como "activa" sin evidencia (test/CI/config verificable).

### Testing

- Rate limiting: test de que el N+1 en la ventana devuelve `429`.
- Logging: aserción de que los logs de eventos de seguridad no contienen PII/secretos.
- CI: gitleaks/SAST verde (`0` secretos, `0` críticos) es el gate de AC-E6.4.2.

### Project Structure Notes

- MODIFICADO principal: `ApiGateway/Program.cs` (rate limiter, HSTS/HTTPS, CORS). Doc de seguridad en `docs/`.
- Auditoría transversal de validators (matchTimeout de Regex) y de logging (sin PII).

### References

- [epics.md — Story 6.4 (AC-E6.4.1/2) + nota de alcance](../planning-artifacts/epics.md)
- [security-and-quality.md — 8 prácticas, reglas de código, cumplimiento](../specs/spec-hotel-booking-hub/security-and-quality.md)
- [architecture.md — Infrastructure & Deployment, CI/CD](../planning-artifacts/architecture.md)
- [Story 6.1](6-1-autenticacion-jwt-oidc.md), [Story 6.2](6-2-autorizacion-por-rol-rbac.md), [Story 6.3](6-3-aislamiento-entre-agentes.md)

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List

### Change Log
