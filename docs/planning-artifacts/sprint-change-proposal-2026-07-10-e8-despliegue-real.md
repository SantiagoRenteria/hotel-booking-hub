# Sprint Change Proposal — Épica 8: cruzar la compuerta (despliegue real + CD)

**Fecha:** 2026-07-10 · **Origen:** correct-course tras party-mode (Winston/John/Amelia) + decisión de Santiago · **Alcance:** Moderate (reorganización de backlog, sin replan fundamental)

## 1. Issue Summary

La Épica 8 se cerró entregando IaC Terraform **validada pero no aplicada** ("compuerta de Fase 3"). Santiago decidió **cruzar la compuerta**: desplegar de verdad a Azure y probar el sistema end-to-end en la nube, con un camino de aprovisionamiento automatizable y `main` protegida. Restricción central: **mínimo costo** (es una prueba técnica, no un entorno permanente).

## 2. Impact Analysis

- **Epic Impact:** Epic 8 reabierta (`done → in-progress`). No afecta épicas 1-7/9 (cerradas). Epic T (entrega) queda **después** por decisión de Santiago (John recomendó antes; riesgo aceptado).
- **Story Impact:** +2 historias — **8.2** (despliegue real de bajo costo + smoke E2E + destroy) y **8.3** (CD on-demand OIDC + approval + protección de `main`). 8.1 sin cambios (done).
- **Artifact Conflicts:** `epics.md` (nuevas historias + nota de épica), `decisions-adr.md` (ADR-021/022/023), `sprint-status.yaml` (reapertura + 8-2/8-3), `DOCUMENTO-BASE.md` §8.13 (nota de compuerta cruzada).
- **Technical Impact:** Terraform (min_replicas, SQL GP_S serverless, ACR), script bootstrap de state, `.github/workflows/cd.yml` (OIDC), migraciones EF Core en pipeline, branch protection (ejecuta Santiago vía `gh`).

## 3. Recommended Approach

**Direct Adjustment** — añadir 8.2/8.3 dentro de la Épica 8 existente. Decisiones cerradas (ver ADR-021/022/023 y memoria `e8-despliegue-real-cd-decision`):

| Tema | Decisión |
|---|---|
| Región / ciclo | East US 2 · apply → smoke → destroy (RG-app efímero) |
| Auth CD | OIDC federated + Environment `production` approval; variables no-secretas |
| State | Script bootstrap `az` + backend `azurerm` AAD + 2 RGs |
| Disparo | `workflow_dispatch` on-demand (deploy y destroy); NO auto en merge |
| Worker | `min=1` (cero-secretos); gateway/hoteles/reservas `min=0` |
| Registro | ACR Basic (pull por Managed Identity) vía `az acr build` |
| SQL | GP_S serverless auto-pause, ambas BD; smoke con retry |
| Migraciones | Script EF idempotente por AAD; runner AAD admin; firewall efímero |
| Gate `main` | branch protection: PR + required checks; aplicada por Santiago (`gh`) |

**Riesgo/esfuerzo (Amelia):** 8.2 = M, 8.3 = M-L. Trabajo **infra/ops** (gate = apply+smoke reproducible, no TDD). Riesgos vivos: cold start SQL, quota ACA en suscripción nueva, olvido de destroy.

## 4. Detailed Change Proposals

Aplicados directamente a los artefactos (modo batch):
- **epics.md:** + Story 8.2, + Story 8.3, nota de cabecera de Épica 8 actualizada.
- **decisions-adr.md:** + ADR-021 (CD OIDC on-demand), + ADR-022 (state remoto), + ADR-023 (scale-to-zero selectivo).
- **sprint-status.yaml:** epic-8 → in-progress; 8-2, 8-3 → backlog.
- **DOCUMENTO-BASE.md §8.13:** nota "[Corregido]" de compuerta cruzada.

## 5. Implementation Handoff

- **Scope:** Moderate → Developer (Amelia) implementa 8.2 luego 8.3 por el protocolo de historia (create-story → dev-story → code-review → PR a develop → merge).
- **Excepción de seguridad:** la protección de `main` la ejecuta **Santiago** con el comando `gh` que se le entregará (el agente no modifica controles de acceso del repo).
- **Ejecución del apply/destroy:** el agente, con la sesión `az` de Santiago, con OK explícito antes de crear recursos facturables.
- **Éxito:** deploy verde + `/health` 200 + flujo de negocio E2E + evento propagado + `destroy` limpio + evidencia; CD reproducible por `workflow_dispatch` con approval; `main` protegida.
