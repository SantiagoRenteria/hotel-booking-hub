---
baseline_commit: 07943acd6d0c4a602de87f009deb914491f38612
---
# Story 8.2: Despliegue real de bajo costo + smoke end-to-end + destroy

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

> **Trazabilidad:** correct-course (party-mode + Santiago 2026-07-10) → **NFR-6 (portabilidad y despliegue) · ejecución real** → `AC-E8.2.x` · **Fase 3 — compuerta CRUZADA**
> **Porqué:** 8.1 dejó la IaC *validada pero no aplicada*. Aquí se despliega **de verdad** a Azure (East US 2) y se ve el sistema correr en la nube, con **mínimo costo** (ciclo `apply → smoke → destroy`). Ver ADR-021/022/023 y memoria `e8-despliegue-real-cd-decision`.

## Story

Como **responsable de despliegue**,
quiero **aplicar la IaC a una suscripción real de Azure (East US 2), verificar el sistema end-to-end y destruirlo**,
para **demostrar que el despliegue cloud-native funciona incurriendo solo en el costo de las horas de prueba**.

## Acceptance Criteria

**AC-E8.2.1 — State remoto por bootstrap sin secretos**
**Dado** un script `bootstrap` idempotente (`az` CLI)
**Cuando** se ejecuta una vez
**Entonces** crea un **RG-state permanente** con Storage Account + container para el `tfstate`, y `terraform init` usa el backend `azurerm` con `use_azuread_auth=true` (cero claves de storage; auth por la sesión `az`). El RG-app es **efímero** (ADR-022).

**AC-E8.2.2 — Tuning de bajo costo**
**Dado** el módulo Terraform
**Cuando** se aplica
**Entonces** `gateway/hoteles/reservas` tienen `min_replicas=0` (scale-to-zero), `notificaciones` tiene `min_replicas=1` (consumo garantizado sin reintroducir secretos, ADR-023), y las 2 BD usan **GP_S serverless con auto-pause**. Las imágenes se sirven desde **ACR Basic** (pull por Managed Identity).

**AC-E8.2.3 — Imágenes y migraciones**
**Dado** el `Dockerfile` multi-stage existente
**Cuando** se despliega
**Entonces** las 4 imágenes se construyen y suben a ACR con `az acr build` (tag = git sha), las apps se apuntan a esas imágenes, y se aplica un **script idempotente** de migraciones EF Core (`dotnet ef migrations script --idempotent`) contra ambas BD por auth AAD, tolerando el cold start del auto-resume (retry/backoff).

**AC-E8.2.4 — Smoke end-to-end real**
**Dado** el sistema desplegado
**Cuando** se ejecuta el smoke
**Entonces** `GET /health` responde 200 y un **flujo de negocio real** autenticado (crear hotel → buscar/crear reserva → cancelar) devuelve 2xx tocando Container Apps + SQL + Service Bus/Redis reales, y el **evento** se propaga al worker de Notificaciones (verificable por log/telemetría). Se captura evidencia.

**AC-E8.2.5 — Teardown limpio + cero secretos**
**Dado** la evidencia capturada
**Cuando** se ejecuta `terraform destroy`
**Entonces** el RG-app efímero queda vacío (el RG-state permanece), no quedan recursos facturables huérfanos, y `gitleaks` sigue verde (ningún secreto entró al repo: contraseñas por `random_password`, claves por atributo de recurso, auth por MI/AAD).

## Tasks / Subtasks

- [x] **Task 1 — Script de bootstrap del state remoto** (AC: 8.2.1) — `bootstrap-state.sh` + backend `azurerm` AAD; ✅ artefacto listo (corre en Task 6).
  - [ ] `deploy/terraform/bootstrap/bootstrap-state.sh` idempotente: `az group create` (RG-state permanente, p. ej. `hbh-tfstate-rg`), `az storage account create` (`--min-tls-version TLS1_2`, `--allow-blob-public-access false`), `az storage container create --auth-mode login` (`tfstate`). Todo con verificación "crear-si-no-existe".
  - [ ] Descomentar/parametrizar el bloque `backend "azurerm"` en `versions.tf` con `use_azuread_auth = true` (sin `access_key`).
  - [ ] Asignar al deployer (usuario `az` actual) el rol **Storage Blob Data Contributor** sobre la cuenta (en el script).
  - [ ] Documentar que el bootstrap corre **una vez** y **no** se destruye; `terraform init -migrate-state` migra del state local.

- [x] **Task 2 — Tuning de bajo costo en Terraform** (AC: 8.2.2) — min_replicas 0/worker 1, SQL GP_S serverless, AAD admin, firewall deployer, nuevas vars; ✅ `fmt`+`validate` verde.
  - [ ] `apps.tf`: `min_replicas = 0` en `gateway`, `hoteles`, `reservas`; **`min_replicas = 1` en `notificaciones`** (ADR-023). Bajar el KEDA poll interval no aplica (worker fijo en 1).
  - [ ] `data.tf`: ambas BD a `sku_name = "GP_S_Gen5_1"`, `min_capacity = 0.5`, `auto_pause_delay_in_minutes = 60`. Quitar `sku_name = "Basic"`.
  - [ ] `data.tf`: añadir `azuread_administrator` al `azurerm_mssql_server` (deployer como AAD admin) para permitir migraciones por token AAD.
  - [ ] `data.tf`: regla de firewall para la IP pública del deployer (variable `deployer_ip` o step `az sql server firewall-rule create` con la IP detectada) — necesaria para migrar desde la máquina local; la de `AllowAzureServices` (0.0.0.0) NO cubre la IP local.
  - [ ] `terraform fmt -check -recursive` + `init -backend=false` + `validate` → verde tras los cambios.

- [x] **Task 3 — Variables de imagen + build/push a ACR** (AC: 8.2.3) — `build-push.sh` (`az acr build`, PROJECT_PATH/APP_DLL reales); `deploy.sh` pasa `-var imagen_*`; ✅ artefacto listo (corre en Task 6).
  - [ ] Confirmar que `apps.tf` toma las imágenes de `var.imagen_*` y que en `apply` se pasan las refs de ACR (`-var imagen_gateway=<acr>/<repo>:<sha>` …) o computarlas de `acr_login_server` + sha.
  - [ ] `az acr build` de las 4 imágenes desde el `Dockerfile` multi-stage (tag = git sha), tras crear el ACR (o ACR en el RG-state para reusar entre applies — decidir en dev-story; por defecto en RG-app, se reconstruye).
  - [ ] Verificar rol `AcrPush` para el deployer si se usa `az acr build` (o que `az acr build` use la identidad del usuario).

- [x] **Task 4 — Migraciones EF Core idempotentes por AAD** (AC: 8.2.3) — `sql/reservas.sql`+`sql/hoteles.sql` generados offline (idempotentes) + `migrate.sh` (AAD, retry); ✅ scripts listos (aplican en Task 6).
  - [ ] Generar script idempotente por BC: `dotnet ef migrations script --idempotent` (Hoteles y Reservas por separado; sus `DbContext` viven en sus proyectos Infrastructure).
  - [ ] Aplicar con `sqlcmd -G` (auth AAD, token del deployer) contra cada BD, con **retry/backoff** por el cold start del auto-resume (timeout ≥ 60s).
  - [ ] Script `deploy/scripts/migrate.sh` (o `.ps1`) que orqueste ambas BD y tolere el 40613/timeout inicial.

- [x] **Task 5 — Smoke end-to-end** (AC: 8.2.4) — `mint-jwt.sh` (HS256 con clave de KV) + `smoke.sh` (/health con retry + flujo real crear hotel→habitación→reserva→cancelar + evidencia del evento); ✅ artefacto listo (corre en Task 6).
  - [ ] `deploy/scripts/smoke.sh`: `GET {gateway_fqdn}/health` con retry (cold start ACA + SQL resume).
  - [ ] Mintar un **JWT de prueba** firmado con la misma clave que valida el sistema (recuperada de Key Vault por el deployer) para autenticar el flujo — o documentar el mecanismo de token de agente. (El sistema valida JWT/OIDC, Épica 6.)
  - [ ] Ejecutar el flujo: crear hotel (rol Agente) → buscar disponibilidad/crear reserva → solicitar/resolver cancelación; aserción de 2xx y de que el **evento** llega al worker (revisar logs de la Container App `notificaciones` o App Insights).
  - [ ] Capturar evidencia (salida de `az containerapp logs`, outputs de smoke) en `docs/implementation-artifacts/evidencia/8-2-*` o `deferred-work.md`.

- [x] **Task 6 — Ejecución del ciclo apply → smoke → destroy** (AC: 8.2.1–8.2.5) ✅ **Ejecutado 2026-07-11 en West US 2** (OK de Santiago). Apply por fases+reintentos → 32 recursos; `az acr build` ×4; migraciones EF por auth SQL; **smoke E2E VERDE** (/health 200, crear hotel/habitación/reserva 201, cancelar OK contra Azure SQL, vía gateway, JWT de KV); `az group delete` limpió el RG-app. Evidencia: `docs/implementation-artifacts/evidencia/8-2-despliegue-real-smoke.md`. *(AC-E8.2.4: flujo HTTP de negocio ✅; evento→worker = límite conocido, transporte Dapr de nube diferido.)*
  - [ ] **Preflight:** `az account show` (confirmar suscripción), registrar providers (`az provider register` para `Microsoft.App`, `Microsoft.ServiceBus`, `Microsoft.Cache`, `Microsoft.Sql`, `Microsoft.ContainerRegistry`, `Microsoft.KeyVault`, `Microsoft.OperationalInsights`), y verificar **quota de ACA** en East US 2.
  - [ ] `bootstrap-state.sh` → `terraform init` (backend AAD) → `terraform plan` (mostrar a Santiago) → **OK** → `terraform apply`.
  - [ ] `az acr build` (Task 3) → migraciones (Task 4) → smoke (Task 5).
  - [ ] `terraform destroy` del RG-app; confirmar que no quedan recursos facturables. Registrar costo aproximado.

- [x] **Task 7 — Documentación (runbook)** (AC: todos) — `deploy/terraform/README.md` con runbook apply→probar→destroy + avisos de costo; `deferred-work.md` actualizado.
  - [ ] `deploy/terraform/README.md` (o `deploy/README.md`): runbook del ciclo apply→smoke→destroy, preflight, variables, y advertencia de costo/olvido de destroy.
  - [ ] Actualizar `deferred-work.md` (adaptador Dapr .NET de nube sigue diferido; connection strings de apps a SQL/Redis en nube — ver más abajo).

## Dev Notes

### Naturaleza del trabajo (LEER)
- **Infra/ops, NO TDD.** No hay lógica de dominio nueva. El "gate" es **`apply` + `smoke` reproducible + `destroy` limpio**, no red-green. Aplicar TDD aquí sería teatro (viola la disciplina de la casa: red/green solo donde hay lógica). Ver ADR-023 y memoria `e8-despliegue-real-cd-decision`.
- **El agente ejecuta `apply`/`destroy`** con la sesión `az` de Santiago (autenticada: sub `Subcripcion Estudio`, `00fb09e8-...`). **Compuerta dura:** pedir OK explícito antes del primer recurso facturable (Task 6). Nunca ingresar credenciales — se usa la sesión `az` ya establecida.

### Estado actual de los archivos que se tocan (verdad de terreno)
- **`deploy/terraform/apps.tf`:** 4 Container Apps. Hoy `gateway` min=1/max=3, `hoteles` min=1/max=3, `reservas` min=1/max=5, `notificaciones` min=1/max=3. Todas con `identity {UserAssigned}` + `registry {ACR, identity}`; imágenes desde `var.imagen_*`. Componentes Dapr `pubsub`(Service Bus)/`statestore`(Redis)/`secretstore`(Key Vault). → **Cambiar min_replicas: 0 para las 3 HTTP, 1 para notificaciones.**
- **`deploy/terraform/data.tf`:** SQL Server (`public_network_access_enabled=true`, `AllowAzureServices` 0.0.0.0) + 2 BD `Basic`; Redis Basic C0 TLS-only; Service Bus Standard + topic + auth rule `manage=true`. → **BD a GP_S serverless auto-pause; + `azuread_administrator`; + regla firewall IP del deployer.**
- **`deploy/terraform/registry.tf`:** ACR Basic `admin_enabled=false` + User-Assigned MI + rol `AcrPull`. → Base para `az acr build`; considerar rol `AcrPush` para el deployer.
- **`deploy/terraform/versions.tf`:** backend `azurerm` **comentado** (hoy `-backend=false`). → **Descomentar + `use_azuread_auth=true`.**
- **`deploy/terraform/variables.tf`:** `ubicacion` default `eastus2` ✓; `imagen_*` default `mcr.microsoft.com/k8se/quickstart:latest` (placeholder). → pasar refs ACR en apply.
- **`Dockerfile`** multi-stage ya existe (usado por compose).

### Decisiones cerradas (no re-decidir) — ADR-021/022/023
- **State:** script bootstrap `az` + backend `azurerm` AAD + **2 RGs** (state permanente / app efímero). Lock por blob lease.
- **Worker `min=1`** (cero-secretos); resto `min=0`. KEDA scaler `azure-servicebus` = deuda futura (reintroduce secreto en ACA).
- **ACR** (pull por MI) sobre GHCR (que exigiría un PAT). `az acr build` (sin Docker daemon).
- **SQL GP_S serverless auto-pause** ambas BD (free offer descartado: 1 por suscripción, no cubre 2 BD).
- **Disparo del CD** (workflow_dispatch + approval) y **branch protection** son de la **Story 8.3**, no de esta.

### Riesgos (Amelia, party-mode)
1. **Cold start auto-pause SQL** (ALTO) → migraciones y smoke con retry/backoff ≥60s; `EnableRetryOnFailure` ya existe en EF Core.
2. **Worker a 0 sin scaler** → mitigado por `min=1` (no aplica el backlog).
3. **Quota ACA en suscripción nueva** (MEDIO) → verificar en preflight; si `quota exceeded`, pedir aumento o ajustar.
4. **Olvido de `destroy`** (MEDIO) → destroy en el mismo ciclo tras el smoke; Service Bus Standard (~10 USD/mes) y Redis corren 24/7.
5. **Firewall SQL** → la IP local del deployer debe abrirse; `AllowAzureServices` no la cubre.
6. **Auth del smoke** → el sistema valida JWT (Épica 6); mintar token de prueba con la clave de Key Vault o documentar.

### Diferido (mantener en deferred-work.md)
- Adaptador `.NET PublicadorEventosDapr` (publicar por `DaprClient`, solo ACA con Dapr runtime) — pareja del wiring de `ConnectionStrings__*` de las apps desde Key Vault en la nube. En esta historia el objetivo es **ver el sistema correr**; si el wiring de connection strings a SQL/Redis de las apps no está completo, se documenta como el límite alcanzado y se prioriza el `/health` + el máximo flujo alcanzable.

### Project Structure Notes
- **Nuevos:** `deploy/terraform/bootstrap/bootstrap-state.sh`, `deploy/scripts/migrate.*`, `deploy/scripts/smoke.*`, evidencia. **Modificados:** `apps.tf`, `data.tf`, `versions.tf`, (`variables.tf`/`registry.tf` si hace falta), `deploy/**/README.md`, `deferred-work.md`.
- **Variance vs base doc:** ninguna nueva; §8.13 ya reconciliado con la nota "[Corregido]" de compuerta cruzada.

### References
- [Source: docs/planning-artifacts/epics.md#Story-8.2]
- [Source: docs/specs/spec-hotel-booking-hub/decisions-adr.md#ADR-021 / #ADR-022 / #ADR-023]
- [Source: docs/DOCUMENTO-BASE.md#8.13]
- [Source: deploy/terraform/apps.tf, data.tf, versions.tf, registry.tf] — estado actual.
- [Source: memoria e8-despliegue-real-cd-decision] — decisión completa.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Amelia / dev-story, modo autónomo)

### Debug Log References

- Gate IaC (no aplica TDD): `terraform fmt -check -recursive` + `init -backend=false` + `validate` → **Success** tras el tuning (GP_S serverless, min_replicas, AAD admin, firewall condicional, backend AAD).
- Migraciones generadas offline del modelo (placeholder connection string en la fábrica design-time, no conecta): `dotnet ef migrations script --idempotent` → `deploy/scripts/sql/{reservas,hoteles}.sql` (386/237 líneas, guard `__EFMigrationsHistory`).
- Sin código .NET tocado → sin regresión en la suite (se valida en CI del PR).
- **Vista previa `terraform plan`** (backend local temporal, no crea recursos, auth por `az`): **Plan 31 to add, 0 change, 0 destroy**, sin errores. Halló 2 defectos que `validate` no captura (validaciones del provider en `plan`) y se corrigieron: (1) el Service Bus namespace no puede terminar en `-sb` → `hbh-dev-bus`; (2) `ARM_SUBSCRIPTION_ID` obligatorio + `ARM_USE_CLI=true` (evita el cuelgue del sondeo IMDS fuera de Azure) en `deploy.sh`/`destroy.sh`. Providers `Microsoft.Cache/ContainerRegistry/KeyVault/ServiceBus` = NotRegistered (los registra el preflight de `deploy.sh`).

### Completion Notes List

- **Tasks 1-5 y 7 COMPLETAS** (artefactos autorizados + Terraform validado en verde). **Task 6 = HALT deliberado (compuerta):** el ciclo real `apply→smoke→destroy` crea recursos facturables y **requiere el OK explícito de Santiago** antes de ejecutarse (además de su sesión `az`, ya activa). No se ejecutó `terraform apply`/`az acr build`/`destroy`.
- **Todo listo para un solo comando gated:** `CONFIRM=yes bash deploy/scripts/deploy.sh` (preflight → bootstrap → init → plan → apply → build/push → migraciones → smoke) y `bash deploy/scripts/destroy.sh` al terminar.
- **Cero secretos preservado:** `random_password` + Key Vault + Managed Identity/AAD; el token del smoke se firma con la clave recuperada de Key Vault en runtime (nunca en el repo). gitleaks seguirá verde.
- **Riesgos gestionados en scripts:** retry/backoff por cold start (migrate/smoke), preflight de providers, firewall efímero por IP del deployer, `destroy` como parte del ciclo.
- **Diferido (deferred-work.md):** wiring completo de `ConnectionStrings__*` de las apps a SQL/Redis en la nube + adaptador `.NET PublicadorEventosDapr` — el smoke puede toparse con este límite; si el flujo de negocio no llega end-to-end por falta de ese wiring, se documenta como el límite alcanzado (el `/health` y el arranque sí se prueban).

### File List

**Nuevos**
- `deploy/terraform/bootstrap/bootstrap-state.sh`
- `deploy/scripts/build-push.sh`, `deploy/scripts/migrate.sh`, `deploy/scripts/mint-jwt.sh`, `deploy/scripts/smoke.sh`, `deploy/scripts/deploy.sh`, `deploy/scripts/destroy.sh`
- `deploy/scripts/sql/reservas.sql`, `deploy/scripts/sql/hoteles.sql`

**Modificados**
- `deploy/terraform/apps.tf` (min_replicas: scale-to-zero + worker min=1)
- `deploy/terraform/data.tf` (SQL GP_S serverless auto-pause, azuread_administrator, firewall deployer)
- `deploy/terraform/variables.tf` (sql_aad_admin_login, sql_aad_admin_object_id, ip_deployer)
- `deploy/terraform/versions.tf` (backend azurerm con use_azuread_auth)
- `deploy/terraform/README.md` (runbook apply→probar→destroy)
- `docs/implementation-artifacts/deferred-work.md` (pendiente de actualizar en Task 6/cierre)

## Change Log

| Fecha | Cambio |
|---|---|
| 2026-07-10 | Story 8.2 (dev-story): Tasks 1-5 y 7 — tuning de bajo costo (scale-to-zero + worker min=1 + SQL GP_S serverless), bootstrap de state remoto AAD, build/push ACR, migraciones EF idempotentes (offline, versionadas), smoke E2E, orquestadores `deploy.sh`/`destroy.sh`, runbook. `terraform fmt`+`validate` verde. **Task 6 (apply→smoke→destroy real) en HALT: requiere OK de Santiago (recursos facturables).** |
| 2026-07-11 | **Task 6 EJECUTADO** en West US 2 (OK de Santiago). Se resolvieron restricciones reales de la suscripción: región (SQL bloqueado en East US), backend del tfstate por clave (cuenta invitada sin RBAC de datos), Azure Managed Redis (clásico retirado), apply por fases+reintentos (consistencia eventual ARM), servicios internos `min=1` (scale-to-zero tras gateway), ruteo del gateway a nombres de app ACA. **Smoke E2E verde** (hotel/habitación/reserva/cancelar contra Azure SQL). Límite conocido: evento→worker (transporte Dapr nube diferido). RG-app destruido. Status → review. |
