# Story 8.3: CD automático por Terraform + protección de `main`

Status: done
baseline_commit: 2b46351

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

> **Trazabilidad:** correct-course (party-mode + Santiago) + ajuste 2026-07-11 → **NFR-6 · gobernanza de entrega** → `AC-E8.3.x` · **Fase 3**
> **Porqué:** formaliza el despliegue ya probado en 8.2 como pipeline **reproducible**. El gate humano es la **aprobación de PR en `main`** (branch protection) + OIDC passwordless.
>
> ⚠️ **REVERSIÓN (T.2, 2026-07-11):** este documento describe el auto-apply al merge a `main` que se implementó originalmente en la 8.3. **Ese comportamiento fue revertido en la historia T.2**: el disparo del CD volvió a ser **on-demand** (`workflow_dispatch`), se eliminó el trigger `push: main` de `cd.yml`, y **mergear a `main` ya NO despliega**. Lee las menciones a "auto-apply en merge" de abajo como contexto histórico; el estado vigente es on-demand (ADR-021, `deploy/terraform/README.md` §CD).

## Story

Como **responsable de entrega**,
quiero **que al aprobar y mergear a `main` el despliegue a Azure ocurra automáticamente por Terraform (OIDC, sin secretos), con `main` protegida bajo mi aprobación**,
para **desplegar de forma auditada, reproducible y sin intervención manual, con un único gate humano (la revisión de PR)**.

## Acceptance Criteria

**AC-E8.3.1 — `main` protegida bajo aprobación**
**Dado** el repositorio
**Cuando** se configura la protección de rama
**Entonces** `main` exige **PR con ≥1 aprobación** (Santiago), **required status checks** verdes y actualizados (`Build · Format · Test`, `Terraform · fmt + validate`, `Secret scan (gitleaks)`), sin push/force-push directo, aplicado también a admins. *(La regla la aplica **Santiago** con el comando `gh` entregado — el agente no modifica controles de acceso del repo.)*

**AC-E8.3.2 — CD automático en merge a `main` (OIDC, passwordless)**
**Dado** `.github/workflows/cd.yml`
**Cuando** ocurre un push a `main` (merge de una PR aprobada)
**Entonces** el workflow autentica por **OIDC federated credentials** (`azure/login@v2`, `permissions: id-token: write`; solo variables `AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID`, cero secretos), y ejecuta el ciclo de despliegue: bootstrap del state → `az acr build` de las 4 imágenes (tag = git sha) → `terraform apply` por fases + reintentos → migraciones EF → smoke E2E. Reusa la lógica de `deploy/scripts/deploy.sh`.

**AC-E8.3.3 — Teardown on-demand**
**Dado** el mismo workflow (o uno hermano)
**Cuando** se dispara por `workflow_dispatch` con acción `destroy`
**Entonces** ejecuta `deploy/scripts/destroy.sh` (o `terraform destroy`) para dejar la suscripción limpia — contraparte del ciclo de mínimo costo.

**AC-E8.3.4 — Setup passwordless documentado y ejecutable por Santiago**
**Dado** que el agente no crea credenciales ni App Registrations (regla de seguridad)
**Cuando** se prepara el CD
**Entonces** se entregan los **comandos `az`/`gh`** para: crear la App Registration + Service Principal, el **federated credential** (subject atado a `ref:refs/heads/main` y/o environment `production`), los role assignments (Contributor + User Access Administrator sobre la suscripción/RG), y las **variables del repo** (`AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID`). Santiago los ejecuta.

## Nota de alcance (LEER)

- **El agente NO ejecuta** el `terraform apply` del CD (depende de las credenciales OIDC que configura Santiago) ni aplica la branch protection ni crea la App Registration (reglas de seguridad: credenciales / controles de acceso). El gate reproducible de esta historia = **workflow válido + comandos de setup entregados + branch protection aplicada por Santiago**.
- **Reutilización:** el CD reusa `deploy/scripts/{deploy,destroy,build-push,migrate,smoke}.sh` y `deploy/terraform/*` ya probados en 8.2. El workflow es un envoltorio fino que exporta las variables ARM desde OIDC y llama a `deploy.sh`/`destroy.sh`.
- **Costo:** el auto-apply en merge a `main` crea infra facturable. Mitigación: `main` sólo recibe merges de *release* (no cada commit); el `destroy` on-demand cierra el ciclo; se documenta el aviso de costo. *(Alternativa más conservadora — Environment `production` con approval antes del apply — queda como endurecimiento opcional; Santiago eligió auto-apply tras la aprobación de PR.)*
- **Límite conocido (heredado de 8.2):** evento→worker no fluye en nube (transporte Dapr→Service Bus diferido).

## Tasks / Subtasks

- [x] **Task 1 — Workflow de CD** (AC: 8.3.2, 8.3.3)
  - [ ] `.github/workflows/cd.yml`: trigger `on: push: branches: [main]` (deploy) + `workflow_dispatch` con input `accion` (`deploy`/`destroy`). `permissions: { id-token: write, contents: read }`, `concurrency` por rama (evita applies solapados).
  - [ ] Job `deploy`: `azure/login@v2` (OIDC, `client-id`/`tenant-id`/`subscription-id` desde `vars`), setup Terraform + .NET + sqlcmd/openssl, `export ARM_USE_OIDC=true ARM_CLIENT_ID/... ARM_SUBSCRIPTION_ID/...`, luego `CONFIRM=yes bash deploy/scripts/deploy.sh` (auto-detecta IP del runner para el firewall SQL).
  - [ ] Job `destroy` (solo `workflow_dispatch` + `accion=destroy`): `bash deploy/scripts/destroy.sh`.
  - [ ] Adaptar `deploy.sh`/`destroy.sh` para auth OIDC en CI: cuando `ARM_USE_OIDC=true`, NO exportar `ARM_ACCESS_KEY` por `az storage keys` (el SP quizá no tenga esa vía) → el backend usa OIDC/`use_azuread_auth` o el SP con rol de datos. *(Decisión de dev-story; mantener la vía por clave para el flujo local del agente.)*

- [x] **Task 2 — Guía/commandos de setup OIDC** (AC: 8.3.4)
  - [ ] `deploy/README.md` (o `docs/`): comandos `az ad app create` + `az ad sp create` + `az ad app federated-credential create` (issuer `https://token.actions.githubusercontent.com`, subject `repo:SantiagoRenteria/hotel-booking-hub:ref:refs/heads/main` y/o `:environment:production`, audience `api://AzureADTokenExchange`), `az role assignment create` (Contributor + User Access Administrator), y `gh variable set AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID`.
  - [ ] Documentar que estos los ejecuta **Santiago** (crean credenciales/identidades).

- [x] **Task 3 — Protección de `main`** (AC: 8.3.1)
  - [ ] Entregar el comando `gh api ... /branches/main/protection` (PR + 1 review + required checks + enforce_admins + no force-push). **Ya entregado**; incluirlo en el README de despliegue para referencia.

- [x] **Task 4 — CI/validación** (AC: todos)
  - [ ] Validar el `cd.yml` (sintaxis YAML / `actionlint` si está disponible); asegurar que NO rompe el `ci.yml` existente. Sin secretos (gitleaks verde).
  - [ ] Documentar en `deferred-work.md` que el primer `apply` del CD requiere el setup OIDC de Task 2 (una vez).

## Dev Notes

### Naturaleza del trabajo
- **Infra/ops, NO TDD.** Gate = workflow válido + comandos de setup + branch protection aplicada. El `apply` real del CD depende de credenciales OIDC que configura Santiago (no ejecutable por el agente).

### Contexto vivo (de 8.2, ya probado verde)
- Despliegue real verificado en **West US 2**: `deploy/scripts/deploy.sh` (apply por fases+reintentos, `az acr build`, migraciones por auth SQL, smoke E2E), `destroy.sh`, `bootstrap-state.sh`, `deploy/terraform/*`. El CD debe **reusarlos**, no reimplementarlos.
- Restricciones de la suscripción ya resueltas en el módulo TF: región westus2, Azure Managed Redis, servicios internos `min=1`, ruteo del gateway a nombres de app ACA, backend del tfstate por clave (para el flujo local; en CI evaluar OIDC/`use_azuread_auth`).

### Decisiones (ADR)
- **ADR-021 (refinado):** CD por OIDC federated; gate humano = **aprobación de PR en `main`**; auto-apply en merge. `workflow_dispatch` para destroy. *(Actualizar el ADR-021 en `decisions-adr.md`.)*

### Restricción de seguridad del agente
- NO crear App Registration / federated credential (credenciales), NO aplicar branch protection (control de acceso del repo). Entregar comandos `az`/`gh`; Santiago los ejecuta.

### References
- [Source: docs/planning-artifacts/epics.md#Story-8.3]
- [Source: docs/specs/spec-hotel-booking-hub/decisions-adr.md#ADR-021]
- [Source: deploy/scripts/deploy.sh, destroy.sh] — lógica reusable del ciclo.
- [Source: .github/workflows/ci.yml] — checks a exigir en la protección de `main`.
- [Source: memoria e8-despliegue-real-cd-decision] — decisión y aprendizajes de 8.2.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Amelia / dev-story, modo autónomo)

### Debug Log References

- `cd.yml` validado como YAML (jobs deploy/destroy; triggers push:main + workflow_dispatch; `if` correctos). Sin secretos (solo `vars.AZURE_*`, gitleaks verde).
- `deploy.sh`/`destroy.sh` se reusan sin cambios: `azure/login@v2` (OIDC) establece la sesión `az` que `ARM_USE_CLI=true` aprovecha; el SP (Contributor) lista la clave del state y lee KV; en Linux el guard `cygpath` de `migrate.sh` no aplica (usa ruta POSIX).

### Completion Notes List

- **CD implementado (Tasks 1-4):** `.github/workflows/cd.yml` — auto-apply en merge a `main` + destroy on-demand (`workflow_dispatch`), OIDC passwordless, reusa los scripts de 8.2; instala `sqlcmd` (go-sqlcmd) en el runner para `migrate.sh`.
- **Setup entregado (no ejecutable por el agente):** comandos `az`/`gh` en `deploy/terraform/README.md` §CD (App Registration + federated credential `environment:production` + roles + variables) y el comando de branch protection de `main`. Los ejecuta Santiago.
- **ADR-021 refinado** (auto-apply en merge + gate de aprobación de PR; Environment `production` como gate opcional).
- **Gate de la historia (infra/ops):** workflow válido + setup documentado + branch protection entregada. El `apply` real del CD depende del setup OIDC de Santiago (diferido, `deferred-work.md`).

### File List

**Nuevos:** `.github/workflows/cd.yml`
**Modificados:** `deploy/terraform/README.md` (§CD), `docs/specs/spec-hotel-booking-hub/decisions-adr.md` (ADR-021 refinado), `docs/implementation-artifacts/deferred-work.md`

## Change Log

| Fecha | Cambio |
|---|---|
| 2026-07-11 | Story 8.3 creada (create-story). Alcance ajustado por Santiago: CD auto-apply en merge a `main` (OIDC) + gate de aprobación de PR + branch protection (comando gh entregado). Reusa los scripts de 8.2. |
| 2026-07-11 | Story 8.3 (dev-story): `cd.yml` (auto-apply merge→main + destroy on-demand, OIDC passwordless), setup OIDC + branch protection documentados (los ejecuta Santiago), ADR-021 refinado. Infra/ops (gate = workflow válido + docs). Status → review. |
