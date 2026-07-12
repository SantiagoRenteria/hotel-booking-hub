# Infraestructura Azure por Terraform (Épica 8 · ADR-008)

IaC del despliegue a Azure de `hotel-booking-hub`, **exclusivamente por Terraform** (sin click-ops). Este doc responde *"qué se provisiona y cómo se aplicaría"*; para el diseño de nube ver `docs/DOCUMENTO-BASE.md` §8.13 y ADR-008/019/020.

## ⚠️ Compuerta de Fase 3 (leer)

La IaC se valida siempre con `terraform fmt -check` + `validate` (local y CI, sin credenciales). **La compuerta se cruza on-demand** (Story 8.2, decisión de Santiago): el despliegue real se ejecuta **cuando se decide**, con estrategia de **mínimo costo `apply → probar → destroy`** — NO auto-aplica en cada merge (ADR-021). Ver el runbook abajo.

## Qué provisiona

| Recurso | Terraform | Rol |
|---|---|---|
| Resource Group | `azurerm_resource_group` | Contenedor de todo |
| Log Analytics + Application Insights | `azurerm_log_analytics_workspace`, `azurerm_application_insights` | Observabilidad (OTel → App Insights, NFR-5) |
| Container Registry | `azurerm_container_registry` | Imágenes de los servicios |
| Managed Identity | `azurerm_user_assigned_identity` | Passwordless: pull de ACR + lectura de Key Vault (ADR-020) |
| SQL Server + 2 BD | `azurerm_mssql_server`, `azurerm_mssql_database` ×2 | Una BD por BC (ADR-001): `db-hoteles`, `db-reservas` |
| Azure Managed Redis | `azurerm_managed_redis` (Balanced_B0) | Caché / idempotencia / state Dapr (ADR-012). Reemplaza al Azure Cache for Redis clásico (retirado). |
| Service Bus (+ topic) | `azurerm_servicebus_namespace`, `_topic` | Transporte del pub/sub Dapr en nube (ADR-019) |
| Key Vault (+ secretos) | `azurerm_key_vault`, `_secret` | Custodia de secretos, RBAC (ADR-020) |
| Container App Environment | `azurerm_container_app_environment` | ACA con Dapr + KEDA gestionados (ADR-008) |
| Componentes Dapr | `azurerm_container_app_environment_dapr_component` | `pubsub` (Service Bus) + `statestore` (Redis) + `secretstore` (Key Vault) |
| 4 Container Apps | `azurerm_container_app` | gateway (ingress externo), hoteles, reservas, notificaciones |

**Bajo costo (Story 8.2 · ADR-023):** `gateway/hoteles/reservas` con `min_replicas=0` (scale-to-zero); `notificaciones` con `min_replicas=1` (consume del Service Bus sin reintroducir secretos). Las 2 BD son **GP_S serverless con auto-pause** (se pausan a 0 de cómputo sin conexiones).

## Cero secretos (ADR-020)

- La contraseña de SQL y la clave JWT se generan con `random_password` (nunca literales en el repo).
- Los secretos (contraseña SQL, cadenas de Service Bus/Redis, clave JWT) viven en **Key Vault**.
- Las apps los leen por **Managed Identity** (rol *Key Vault Secrets User*), sin credenciales en config.
- gitleaks en CI verifica que no haya secretos en el repositorio.

## Cloud-agnostic por Dapr (AC-E8.1.2)

El component Dapr `pubsub` en la nube apunta a **Azure Service Bus**; en local, el mismo `name: pubsub` apunta a **RabbitMQ**. Cambiar el broker **dentro de Dapr** es solo el YAML del component. La selección **local↔nube** del adaptador (`PublicadorEventosRabbitMq` vs `PublicadorEventosDapr`) es el patrón **Strategy por entorno** (ADR-019/020) — el dominio no cambia. *(El adaptador `.NET` de nube requiere Dapr en runtime; queda documentado como el camino de nube, ver la historia 8.1.)*

## Cómo se aplicaría (con credenciales)

```bash
az login                                   # o un Service Principal / OIDC en CI
cd deploy/terraform
# En producción: configurar el backend remoto (ver versions.tf) antes de init.
terraform init
terraform plan  -var 'entorno=dev'
terraform apply -var 'entorno=dev'
# Luego: az acr build/push de las imágenes y actualizar var.imagen_* con la etiqueta publicada.
```

## Gate local / CI

```bash
terraform -chdir=deploy/terraform fmt -check -recursive
terraform -chdir=deploy/terraform init -backend=false
terraform -chdir=deploy/terraform validate
```
El job `terraform` de `.github/workflows/ci.yml` ejecuta exactamente esto en cada push/PR.

## Runbook — despliegue real de bajo costo (Story 8.2)

Estrategia **`apply → probar → destroy`** (pagas solo las horas de prueba). Requiere `az login` activo + Terraform + `dotnet ef` + `sqlcmd` (go-sqlcmd) + `openssl`.

```bash
# 1) Bootstrap del state remoto (una vez; RG-state PERMANENTE, no se destruye) — ADR-022
bash deploy/terraform/bootstrap/bootstrap-state.sh          # crea RG-state + Storage + container (auth AAD)

# 2) Deploy orquestado. Primer pase muestra el PLAN y se detiene (compuerta):
bash deploy/scripts/deploy.sh                               # preflight + bootstrap + init + PLAN
# Revisa el plan y, para crear recursos FACTURABLES:
CONFIRM=yes bash deploy/scripts/deploy.sh                   # apply + build/push ACR + migraciones + smoke

# 3) Al terminar de probar — imprescindible para no incurrir en costos:
bash deploy/scripts/destroy.sh                              # destruye el RG-app (el RG-state permanece)
```

Scripts (`deploy/scripts/` y `deploy/terraform/bootstrap/`):
- `bootstrap-state.sh` — state remoto sin secretos (backend `azurerm` + `use_azuread_auth`).
- `build-push.sh` — `az acr build` de las 4 imágenes (tag = git sha); pull por Managed Identity.
- `sql/*.sql` — migraciones EF Core **idempotentes** (generadas offline del modelo, versionadas).
- `migrate.sh` — aplica los `.sql` por **AAD** (`sqlcmd -G`) con retry por el cold start del auto-resume.
- `mint-jwt.sh` — emite un JWT HS256 de prueba con la clave de Key Vault (réplica de `TokenDePrueba`).
- `smoke.sh` — `/health` con retry + flujo de negocio real (crear hotel → habitación → reserva → cancelar) + evidencia del evento en el worker.
- `deploy.sh` / `destroy.sh` — orquestadores del ciclo (la compuerta `CONFIRM=yes` protege el `apply`).

**Región:** `westus2` (la suscripción de prueba **bloquea Azure SQL en eastus2/eastus**; se verificó con la API de capabilities que `westus2` sí lo permite).

**Costos/avisos:** Azure Managed Redis Balanced_B0 (~0.06–0.10 USD/h) y Service Bus Standard (~0.01 USD/h) **facturan mientras existan**; SQL serverless pausada es marginal (solo storage); ACA scale-to-zero ≈ 0. Por eso el `destroy` es parte del ciclo. Riesgos: cold start (mitigado con retry), quota/restricciones de la suscripción (SQL y algunos SKU premium pueden estar deshabilitados por región — el `plan` no siempre lo detecta, solo el `apply`).

## CD — despliegue continuo (Story 8.3)

`.github/workflows/cd.yml` despliega **on-demand** (`workflow_dispatch` → `accion=deploy`) y permite **teardown on-demand** (`accion=destroy`). **NO auto-aplica al merge a `main`** (ADR-021: John vetó el auto-apply por control de costos — cada apply crea infra facturable). Auth **100% passwordless por OIDC** (cero secretos; solo variables no-secretas). Gates: la aprobación de PR en `main` (branch protection) protege la rama, y `environment: production` puede exigir *required reviewers* como gate previo al apply. El workflow **reusa `deploy/scripts/deploy.sh`/`destroy.sh`**.

> **El apply real requiere este setup una vez** — lo ejecuta **Santiago** (crea credenciales/identidades; el agente no lo hace por política de seguridad).

### 1) OIDC: App Registration + federated credential + roles + variables

```bash
# App Registration + Service Principal para el CD
APP_ID=$(az ad app create --display-name "hbh-github-cd" --query appId -o tsv)
az ad sp create --id "$APP_ID"
SUB=$(az account show --query id -o tsv); TENANT=$(az account show --query tenantId -o tsv)

# Federated credential — el job usa `environment: production`, así que el subject de GitHub es el de environment.
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-env-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:SantiagoRenteria/hotel-booking-hub:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Roles: Contributor (crear recursos) + User Access Administrator (Terraform asigna roles a la Managed Identity)
SP_OID=$(az ad sp show --id "$APP_ID" --query id -o tsv)
az role assignment create --assignee-object-id "$SP_OID" --assignee-principal-type ServicePrincipal --role "Contributor" --scope "/subscriptions/$SUB"
az role assignment create --assignee-object-id "$SP_OID" --assignee-principal-type ServicePrincipal --role "User Access Administrator" --scope "/subscriptions/$SUB"

# Variables del repo (NO secretos)
gh variable set AZURE_CLIENT_ID --body "$APP_ID"
gh variable set AZURE_TENANT_ID --body "$TENANT"
gh variable set AZURE_SUBSCRIPTION_ID --body "$SUB"
```

### 2) Proteger `main` (PR + tu aprobación + checks)

```bash
gh api -X PUT /repos/SantiagoRenteria/hotel-booking-hub/branches/main/protection --input - <<'JSON'
{
  "required_status_checks": { "strict": true, "contexts": ["Build · Format · Test", "Terraform · fmt + validate", "Secret scan (gitleaks)"] },
  "enforce_admins": true,
  "required_pull_request_reviews": { "required_approving_review_count": 1, "dismiss_stale_reviews": true },
  "restrictions": null,
  "allow_force_pushes": false,
  "allow_deletions": false
}
JSON
```

### 3) Flujo

`develop → PR a main → (CI verde + tu aprobación) → merge` (la rama queda protegida y sincronizada; **el merge NO despliega**). Para desplegar: Actions → CD → *Run workflow* → `accion=deploy`. Teardown: Actions → CD → *Run workflow* → `accion=destroy`. **Aviso de costo:** el `deploy` on-demand crea infra facturable; el ciclo de mínimo costo es `deploy → probar → destroy`. Endurecimiento opcional: añadir *required reviewers* al Environment `production` (gate extra antes del apply).

## Migración a AKS (documentada, no ejecutada)

ACA corre sobre AKS por debajo. Se migraría a AKS para control fino (ingress controllers, network policies, service mesh) o workloads no-serverless; viable porque Dapr y los contenedores son los mismos (ADR-008).
