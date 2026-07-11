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
| Redis | `azurerm_redis_cache` | Caché / idempotencia / state Dapr (ADR-012) |
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

**Costos/avisos:** Redis Basic C0 (~0.02 USD/h) y Service Bus Standard (~0.01 USD/h) **facturan 24/7 mientras existan**; SQL serverless pausada es marginal (solo storage); ACA scale-to-zero ≈ 0. Por eso el `destroy` es parte del ciclo. Riesgos: cold start (mitigado con retry), quota de ACA en suscripción nueva (el preflight registra providers; si `quota exceeded`, pedir aumento).

## Migración a AKS (documentada, no ejecutada)

ACA corre sobre AKS por debajo. Se migraría a AKS para control fino (ingress controllers, network policies, service mesh) o workloads no-serverless; viable porque Dapr y los contenedores son los mismos (ADR-008).
