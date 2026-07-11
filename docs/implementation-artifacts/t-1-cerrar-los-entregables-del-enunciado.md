# Story T.1: Cerrar los entregables del enunciado

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

> **Trazabilidad:** Requisitos de entrega del enunciado → *(entregables transversales, sin FR)* → `AC-ET.1.x` · **Obligatorio (entrega)**
> **Porqué:** el enunciado exige entregables concretos (repo, README, docs, Postman, compose, ADRs) cuya ausencia se penaliza aunque el core funcione. Es el DoD de entrega. Épicas 1-9 y 8 DONE (incluye despliegue real probado + CD).

## Story

Como **evaluador de la prueba**,
quiero **encontrar todos los entregables exigidos y el razonamiento detrás de las decisiones**,
para **evaluar la solución de forma completa y ágil**.

## Acceptance Criteria

**AC-ET.1.1 — Repositorio público sin dependencias privadas**
Repo público, compila sin paquetes privados (ADR-009), sin secretos (gitleaks verde).

**AC-ET.1.2 — README enrutador con "Decisiones y por qué"**
`README.md` raíz con: tabla **"Decisiones y por qué"** (5-7 decisiones → trade-off → enlace al ADR), **C4 de contenedores** (mermaid), **árbol de carpetas comentado**, tabla enrutadora ("si quieres X → ve a Y"). Sin duplicar contenido; `epics.md` referenciado pero no destacado.

**AC-ET.1.3 — Documentación de seguridad y de uso de IA**
`docs/`: **prácticas de seguridad** (8 prácticas → OWASP, con el porqué; JWT/RBAC/aislamiento IDOR/cero-secretos/rate-limit/headers) y **uso de IA** (flujo BMAD, agentes, party-mode, prompts críticos, iteración/verificación).

**AC-ET.1.4 — Colección Postman ejecutable en CI (Newman)**
Colección Postman del flujo de negocio (crear hotel→habitación→reserva→cancelar) + casos negativos (400/401/403/404/409), ejecutable con **Newman** en CI. Reusa payloads reales validados en 8.2 (enums numéricos; documento `^[A-Za-z0-9\-]{4,20}$`; teléfono `^\+?\d{7,15}$`).

**AC-ET.1.5 — `docker-compose` funcional (data-plane, verificado por smoke)**
`docker compose up` + crear reserva fluye **end-to-end LOCAL**: cablear `ConnectionStrings__hotelesdb/reservasdb/redis` en `deploy/docker-compose.yml` (los servicios `sql-hoteles`/`sql-reservas`/`redis` ya existen; `rabbitmq` ya cableado en E9). Transporte LOCAL = RabbitMQ directo (NO Dapr, ADR-019). El smoke de CI (`docker compose up` + `/health`) sigue verde y detecta drift.

**AC-ET.1.6 — ADRs como documento (`docs/adr/`)**
ADR como archivos individuales (Contexto · Decisión · Consecuencias), incluidos los de E8 (ADR-021 CD OIDC, ADR-022 backend por clave, ADR-023 scale-to-zero selectivo) y los previos (015/016/017/018/019/020). Enlazados desde el README.

## Tasks / Subtasks

- [ ] **Task 1 — Data-plane funcional del compose** (AC: ET.1.5)
  - [ ] `deploy/docker-compose.yml`: añadir a `hoteles` `ConnectionStrings__hotelesdb` (→ `sql-hoteles`), a `reservas` `ConnectionStrings__reservasdb` (→ `sql-reservas`) + `ConnectionStrings__redis` (→ `redis:6379`), a `notificaciones` `ConnectionStrings__redis`. Cadena SQL: `Server=<svc>;Database=db-<bc>;User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True`.
  - [ ] Aplicar migraciones EF al arranque (Program.cs `Database.Migrate()` gated por entorno/flag, o un init) para que el esquema exista sin pasos manuales. Verificar que NO rompe los tests (que usan Testcontainers `MigrateAsync`).
  - [ ] Verificar local: `docker compose up` → crear hotel→habitación→reserva→cancelar 2xx + notificación por RabbitMQ (log del worker). Documentar.

- [ ] **Task 2 — README enrutador + C4** (AC: ET.1.2)
  - [ ] `README.md` raíz: tabla "Decisiones y por qué" (5-7 con enlace a `docs/adr/`), C4 de contenedores (mermaid), árbol comentado, tabla enrutadora. Sin duplicar; enlazar SPEC/PRD/architecture/epics con su letrero de navegación.

- [ ] **Task 3 — ADRs como archivos** (AC: ET.1.6)
  - [ ] `docs/adr/ADR-0XX-*.md` por cada ADR (extraer de `docs/specs/spec-hotel-booking-hub/decisions-adr.md`), con Contexto·Decisión·Consecuencias. Índice `docs/adr/README.md`. Incluir ADR-021/022/023 (E8). Enlazar desde el README.

- [ ] **Task 4 — Docs de seguridad + uso de IA** (AC: ET.1.3)
  - [ ] `docs/seguridad.md`: 8 prácticas → OWASP + porqué (reusar Épica 6). `docs/uso-de-ia.md`: flujo BMAD, agentes, party-mode, prompts críticos, iteración/verificación (incluye la saga real de E8: restricciones de la sub y cómo se resolvieron).

- [ ] **Task 5 — Postman/Newman en CI** (AC: ET.1.4)
  - [ ] `postman/hotel-booking-hub.postman_collection.json` (+ environment): flujo feliz + negativos, con auth JWT. Job de CI que corre `newman run` (contra el compose levantado, reusando el smoke). Reusar payloads reales de `deploy/scripts/smoke.sh`.

- [ ] **Task 6 — Repo/limpieza final** (AC: ET.1.1)
  - [ ] Verificar repo público, sin paquetes privados, gitleaks verde, `.gitignore` correcto. Revisar el enunciado para cualquier entregable faltante.

## Dev Notes

### Naturaleza del trabajo
- Mixto **docs + infra local** (no TDD, salvo que Task 1 toque Program.cs → cuidar que la suite de 450 tests siga verde). Gate: `docker compose up` E2E local + Newman verde en CI + docs presentes.

### Aprendizajes reales de E8 (incluir en docs)
- Despliegue nube **probado de verdad** (8.2, West US 2): restricciones de la suscripción "Estudio" y cómo se resolvieron — SQL bloqueado en East US (→ westus2), Azure Cache for Redis clásico retirado (→ Azure Managed Redis), cuenta invitada sin RBAC de datos (→ backend tfstate por clave, ADR-022), consistencia eventual de ARM (→ apply por fases+reintentos), scale-to-zero de servicios internos poco fiable tras el gateway (→ min=1, ADR-023), ruteo del gateway a nombres de app ACA. CD por OIDC + protección de main (8.3, ADR-021).
- **Límite conocido:** evento→worker no fluye en nube (transporte Dapr→Service Bus diferido); en LOCAL sí (RabbitMQ, E9) — el compose data-plane de Task 1 lo hace verificable localmente.

### Convenciones
- Contrato por **claves de configuración** (`ConnectionStrings__*`) + puerto `IPublicadorEventos`: mismo contrato local/nube, distinto proveedor por entorno. NO Dapr en local (ADR-019).
- `docker-compose.yml` ya cablea `ConnectionStrings__rabbitmq` y `Jwt__SigningKey` (desde `.env` gitignored); Task 1 añade SQL/Redis.

### Project Structure Notes
- **Nuevos:** `README.md` (raíz, reescritura), `docs/adr/*`, `docs/seguridad.md`, `docs/uso-de-ia.md`, `postman/*`, job Newman en `ci.yml`.
- **Modificados:** `deploy/docker-compose.yml` (SQL/Redis), quizá `src/Servicios/*/*.Api/Program.cs` (migrate al arranque, gated).

### References
- [Source: docs/planning-artifacts/epics.md#Story-T.1] · [Source: docs/DOCUMENTO-BASE.md]
- [Source: docs/specs/spec-hotel-booking-hub/decisions-adr.md] — origen de los ADR a volcar.
- [Source: deploy/docker-compose.yml, deploy/scripts/smoke.sh] — data-plane + payloads reales.
- [Source: memoria e8-despliegue-real-cd-decision] — aprendizajes de E8 para las docs.

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
