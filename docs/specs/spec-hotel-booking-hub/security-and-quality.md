# Seguridad, calidad y cumplimiento

Compañero de [SPEC.md](SPEC.md). Sostiene CAP-8 (auth + RBAC) y las constraints de seguridad y calidad.

## Defensa en profundidad — 8 prácticas mapeadas a OWASP Top 10 (2021)

El enunciado pide JWT/OAuth2 + ≥3 prácticas; se implementan **8**:

| Práctica | Implementación | OWASP Top 10 |
|----------|----------------|--------------|
| 1. AuthN — JWT/OIDC | issuer/audience/expiración + refresh tokens; roles `Agente`/`Viajero` | A07 Identification & Auth Failures |
| 2. AuthZ — RBAC server-side | policies nativas .NET; el agente solo gestiona *sus* hoteles/reservas | A01 Broken Access Control |
| 3. Rate limiting | `AddRateLimiter` (sliding window) en el Gateway; protege login y búsqueda | A07 / A04 |
| 4. Validación / anti-inyección | FluentValidation + EF Core parametrizado; sin SQL dinámico | A03 Injection |
| 5. Manejo seguro de secretos | user-secrets/Dapr local; **Key Vault + Managed Identity** en nube | A02 Cryptographic Failures |
| 6. HTTPS enforcement | HSTS + redirección; TLS en el ingress; CORS allowlist | A05 Security Misconfiguration |
| 7. Logging de eventos de seguridad | login ok/fallido, 403, rate-limit, cambios sensibles; sin PII/secretos | A09 Logging & Monitoring Failures |
| 8. Protección de PII + integridad | cifrado de columnas sensibles; validación de URLs salientes; dependency scanning | A02 / A08 / A10 SSRF |

**Diseño seguro (A04 Insecure Design):** el invariante anti-overbooking y la idempotencia son controles de diseño, no parches.

### Estado de implementación y evidencia (Story 6.4)

Alcance (party-mode Winston): el mapeo completo al Top 10 se **documenta**; lo aplicable se **ejercita con código**. Estado:

| # Práctica | Estado | Evidencia |
|-----------|--------|-----------|
| 1. AuthN JWT/OIDC | ✅ Código | `Comun.Web/Seguridad/AutenticacionJwtExtensions.cs`; Story 6.1; `Seguridad.FunctionalTests` (401 en el borde) |
| 2. AuthZ RBAC + aislamiento | ✅ Código | `AutorizacionPorRolExtensions.cs` (6.2, 403 por rol) + aislamiento por propietario/agente (6.3, 404 ajeno); `Reservas/Hoteles.FunctionalTests` + `AislamientoHotelesTests` |
| 3. Rate limiting | ✅ Código | `ApiGateway/Program.cs` (`AddRateLimiter` sliding window → 429); `RateLimitGatewayTests` |
| 4. Validación / anti-inyección | ✅ Código (existente) | 12 `AbstractValidator` (FluentValidation → 400); EF Core parametrizado (0 `FromSqlRaw`/`ExecuteSql`); `Regex` con `matchTimeout=200ms` (`ExpresionesValidacion.cs`) |
| 5. Manejo de secretos | ✅ Código + CI | Clave JWT por env/user-secrets/Key Vault (nunca en repo); `appsettings` solo no-sensibles; gitleaks en CI (0 hallazgos); `.gitleaks.toml` |
| 6. HTTPS/HSTS + CORS | ✅ Código (HSTS+CORS) · 📄 TLS en ingress | `ApiGateway/Program.cs` (`UseHsts` no-dev; CORS allowlist explícita, nunca `AllowAnyOrigin`); redirección/terminación TLS en el ingress (ACA, ADR-008) |
| 7. Logging de eventos de seguridad sin PII | 📄 Documentado + parcial | 401/403/429 no exponen PII (Problem Details sin datos sensibles); trazas OTel con `trace-id`. Un logger dedicado de eventos (login ok/fallido) se activa al cablear el IdP real (F2) |
| 8. Protección de PII + integridad | 📄 Documentado | PII de huéspedes no se loguea; cifrado de columnas sensibles + dependency scanning = readiness (F2/nube). El pin de CVE (`Microsoft.OpenApi`) evidencia gestión de vulnerabilidades |

**Leyenda:** ✅ ejercitado con código/tests · 📄 documentado (readiness / F2 / ingress). El subconjunto ejercitado (authz/aislamiento, rate limiting, HSTS/CORS, anti-inyección, secretos) cubre el núcleo aplicable; un barrido exhaustivo del Top 10 sería gold-plating para la prueba.

## Observabilidad

- **OpenTelemetry** (traces + metrics + logs) en todos los servicios (OTLP).
- Local: dashboard de Aspire (standalone como contenedor en docker-compose). Nube: Application Insights / Azure Monitor.
- **Trazas distribuidas:** cada request nace con un `trace-id` (W3C Trace Context) propagado Gateway → servicio → sidecar Dapr → broker → worker. Ante un fallo, el *waterfall* de spans muestra el servicio/operación exacto en rojo con su excepción. Logs estructurados (Serilog) enriquecidos con ese `trace-id`.
- **Detección de degradación:** histograma de duración por endpoint, alertas p95/p99, exemplars que ligan métrica ↔ traza.

## Reglas de calidad y seguridad de código (nunca violar)

**Seguridad de código:**
- Prohibido hardcodear secretos/credenciales/connection strings. `appsettings.json` solo valores no sensibles (placeholders `""`).
- Toda `Regex` con `matchTimeout` explícito (anti-ReDoS), entre 100 ms y 2 s.
- HTTP solo en `localhost`; cualquier otro origen → HTTPS. CORS con lista explícita, nunca `AllowAnyOrigin()` en producción.
- Secreto commiteado → marcarlo comprometido y rotarlo.

**Contenedores:** nunca correr como root (`USER` no privilegiado); tags específicos (no `:latest`); multi-stage build; `.dockerignore` completo; `HEALTHCHECK` definido.

**C# / .NET:**
- Capturar excepciones específicas; structured logging con contexto; no tragar excepciones.
- `async`/`await` en todo I/O; `CancellationToken` propagado end-to-end; nada de `.Result`/`.Wait()`.
- `<Nullable>enable</Nullable>`; `using`/`await using` para disposables; `sealed` por defecto.
- Métodos ≤ 50 líneas; complejidad ciclomática ≤ 10; sin *magic numbers*; sin código comentado; sin TODO/FIXME sin referencia.

## Cumplimiento (alineación con la vacante)

- **OWASP Top 10:** cubierto y mapeado arriba.
- **PCI DSS (readiness, no implementado):** la prueba no incluye pagos, pero el diseño está listo — no almacenar PAN (tokenización vía PSP externo), TLS en todo el transporte, cifrado en reposo, logs sin datos de tarjeta, segmentación de red, control de acceso por rol.
- **ISO 27001 (controles mapeados):** control de acceso (A.9 → RBAC), criptografía (A.10 → TLS + cifrado PII + Key Vault), registro y auditoría (A.12 → logging + OTel), gestión de vulnerabilidades (A.12.6 → dependency scanning en CI).

## Checklist antes de commit

```
[ ] dotnet build sin warnings nuevos
[ ] dotnet test 100% verde
[ ] Cobertura de líneas nuevas ≥ 80%
[ ] Sin secretos en el diff (gitleaks)
[ ] Sin issues Sonar nuevos de severidad ≥ Major
[ ] newman run de la colección Postman en verde
[ ] Pre-commit hooks (gitleaks, dotnet format)
```
