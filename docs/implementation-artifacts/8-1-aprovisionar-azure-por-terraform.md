---
baseline_commit: 61a66865bf74d5de62498f8834c21a036feac1e6
---
# Story 8.1: Aprovisionar Azure por Terraform

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

> **Trazabilidad:** — → **NFR-6 (portabilidad y despliegue)** → `AC-E8.1.x` · **Recortable · Fase 3 (con compuerta)**
> **Porqué:** demuestra cloud-native e IaC; el despliegue a Azure es **exclusivamente por Terraform** (ADR-008), sin click-ops. Bajo compuerta: la IaC se entrega **validada y ejecutable**, no **aplicada** (no hay suscripción/credenciales en la prueba).

## Story

Como **responsable de despliegue**,
quiero **aprovisionar la infraestructura de Azure con Terraform**,
para **desplegar de forma reproducible, versionada y sin provisión manual**.

## Acceptance Criteria

**AC-E8.1.1 — IaC ejecutable y sin secretos en código**
**Dado** el módulo Terraform en `deploy/terraform/`
**Cuando** se ejecuta `terraform init -backend=false` + `terraform fmt -check` + `terraform validate`
**Entonces** pasa en verde y el módulo describe **ACA (Container App Environment con Dapr) + Azure SQL (×2 BD) + Cache for Redis + Service Bus + Key Vault + Application Insights (+ Log Analytics + ACR + Managed Identity)**, **sin credenciales/valores sensibles hardcodeados** (contraseñas por `random_password`, secretos en Key Vault). *(`plan`/`apply` requieren auth de Azure → compuerta de Fase 3; documentado, no ejecutado aquí.)*

**AC-E8.1.2 — Cloud-agnostic por Dapr (broker por component)**
**Dado** el component Dapr de pub/sub en la nube
**Cuando** se despliega
**Entonces** el transporte es **Azure Service Bus** declarado como **component Dapr** (`pubsub`), de modo que cambiar RabbitMQ↔Service Bus **dentro de Dapr** es solo el YAML del component; el adaptador `PublicadorEventosDapr` se selecciona por entorno (Strategy, ADR-019/020) sin tocar el dominio. *(La selección local↔nube del adaptador es una línea de DI, no del dominio; ver Nota de alcance.)*

**AC-E8.1.3 — Secretos gestionados (sin exposición)**
**Dado** los secretos (contraseña SQL, cadena de Service Bus/Redis, clave JWT)
**Cuando** se aprovisiona
**Entonces** viven en **Key Vault**, se acceden por **Managed Identity** (passwordless), y los componentes Dapr los resuelven vía **secret store** (`secretKeyRef`) — cero secretos en el repo (ADR-020).

**AC-E8.1.4 — CI valida la IaC**
**Dado** el pipeline de CI
**Cuando** corre
**Entonces** un job de Terraform ejecuta `fmt -check` + `validate` (subset ejecutable sin credenciales) y falla si el HCL no está formateado o es inválido.

## Nota de alcance (LEER — compuerta de Fase 3)

- **`terraform plan`/`apply` NO se ejecutan aquí:** requieren una suscripción de Azure + credenciales (Service Principal / OIDC). El entregable es IaC **validada** (`fmt`+`validate`) y **documentada como ejecutable**; aplicarla es un paso operativo fuera de la prueba (ADR-008, "Fase 3 con compuerta"). Esto NO es "documentado pero roto": Terraform es intrínsecamente describir→validar→aplicar-cuando-haya-auth.
- **Estado remoto (backend):** para la prueba se usa `-backend=false` (validate local); se **documenta** que producción usaría un backend remoto (Azure Storage + state lock). No se configura un backend real.
- **Adaptador `PublicadorEventosDapr` (.NET):** el código del adaptador de nube (publicar vía `DaprClient`) requiere Dapr en runtime (solo en ACA); se **documenta** como el camino de nube por el mismo puerto `IPublicadorEventos` (Strategy). NO se cablea/testea localmente en esta historia (no hay Dapr local; ADR-019). Candidato a seguimiento si se ejecuta un despliegue real.
- **Imágenes:** el `Dockerfile` multi-stage ya existe (usado por compose). El push a ACR y el despliegue real quedan bajo la compuerta.

## Tasks / Subtasks

- [x] **Task 1 — Estructura del módulo Terraform** (AC: 8.1.1) ✅ `deploy/terraform/`: `versions.tf` (terraform ≥1.9, `azurerm ~> 4.20`, `random`; backend remoto documentado, `-backend=false` en validate), `providers.tf`, `variables.tf`, `outputs.tf`, `main.tf` + archivos por dominio.
- [x] **Task 2 — Recursos base + observabilidad** (AC: 8.1.1) ✅ RG, Log Analytics, App Insights (workspace-based, connection string sensible), ACR, User-Assigned Managed Identity (+ rol AcrPull).
- [x] **Task 3 — Datos y mensajería** (AC: 8.1.1, 8.1.2) ✅ SQL Server + 2 BD (`db-hoteles`/`db-reservas`, password por `random_password`), Redis (TLS-only), Service Bus namespace + topic + auth rule. Cadenas → Key Vault.
- [x] **Task 4 — Key Vault + secretos (passwordless)** (AC: 8.1.3) ✅ Key Vault (RBAC), secretos (SQL/JWT/Service Bus/Redis) por `random_password`/atributos de recurso (nunca literal), roles *Secrets User* (identidad) y *Secrets Officer* (deployer).
- [x] **Task 5 — ACA + Container Apps + Dapr** (AC: 8.1.1, 8.1.2) ✅ Container App Environment (Log Analytics, Dapr/KEDA gestionados) + 4 Container Apps (gateway externo; resto interno) con Managed Identity, registry ACR por identidad, `dapr { app_id, app_port }`, min/max replicas, y JWT desde Key Vault (`key_vault_secret_id` + identity). Componentes Dapr `pubsub` (Service Bus) + `statestore` (Redis) con mismos nombres que el local.
- [x] **Task 6 — CI: validar la IaC** (AC: 8.1.4) ✅ Job `terraform` en `ci.yml`: `setup-terraform` 1.14.8 → `fmt -check -recursive` + `init -backend=false` + `validate` sobre `deploy/terraform`. Sin `plan`/`apply`.
- [x] **Task 7 — Documentación** (AC: todos) ✅ `deploy/terraform/README.md` (qué provisiona, cero secretos, cloud-agnostic Dapr, cómo se aplicaría, compuerta, migración AKS). `deferred-work.md` actualizado (adaptador Dapr .NET de nube).

## Dev Notes

### Verdad de terreno

- **`deploy/terraform/` está vacío** (solo `.gitkeep`). **Terraform v1.14.8** disponible localmente (permite `init -backend=false`/`fmt`/`validate`; NO `plan`/`apply` sin Azure).
- **`deploy/dapr/`** tiene `pubsub.yaml` (pubsub.rabbitmq, con `secretKeyRef: rabbitmq-connection`), `statestore.yaml` (state.redis), `local/pubsub.yaml`. Son la referencia de nombres/estructura para los componentes Dapr de ACA (mismo `name: pubsub`/`statestore`).
- **CI** (`.github/workflows/ci.yml`): jobs `build-test`, `gitleaks`, `smoke-compose` (solo en `main`). Añadir el job `terraform` (no depende de .NET).
- **Recursos objetivo** (base doc §8.13, ADR-008): ACA (Dapr + KEDA gestionados) · Azure SQL Database ×2 · Azure Cache for Redis · Azure Service Bus · Key Vault + Managed Identity · Application Insights · ACR.

### Arquitectura y convenciones

- **ADR-008:** Azure Container Apps (Dapr y KEDA gestionados) + Terraform exclusivo, sin click-ops. ACA sobre AKS por debajo; migración a AKS documentada, no ejecutada.
- **ADR-019/020:** en nube el transporte es **Dapr → Service Bus** (component); secretos por **Dapr Secrets + Key Vault**. El adaptador `.NET` se selecciona por entorno (Strategy) — el dominio no cambia.
- **Cero secretos en repo (G6/NFR-4):** contraseñas por `random_password`; secretos en Key Vault; nada literal en HCL ni en el repo. gitleaks en CI seguirá verde.
- **Naming/tags:** prefijo por entorno (`hbh-{env}-*`), tags coherentes (`proyecto`, `entorno`). Regiones/SKU por variable con defaults razonables (SKU baratos: SQL Basic/S0, Redis Basic C0, Service Bus Standard, ACA consumption).

### Testing standards (IaC)

- **No aplica TDD/xUnit** (es IaC declarativa). El "gate" = `terraform fmt -check -recursive` + `terraform init -backend=false` + `terraform validate` (verde), local y en CI. Opcional `tflint` si está disponible (no requerido).
- **No se ejecuta `plan`/`apply`** (compuerta). La validación prueba sintaxis + consistencia de tipos/refs, no el aprovisionamiento real.
- El resto de la suite .NET (450 tests) debe seguir verde (esta historia no toca código .NET; solo `deploy/` + CI + docs).

### Project Structure Notes

- **Nuevos:** `deploy/terraform/*.tf` + `deploy/terraform/README.md`. Modificados: `.github/workflows/ci.yml` (job terraform), README raíz/`deferred-work.md` (referencia).
- **Variance vs base doc:** ninguna nueva — la topología es la de §8.13/ADR-008. El transporte nube por Dapr→Service Bus ya está reconciliado (ADR-019/020).

### References

- [Source: docs/planning-artifacts/epics.md#Story-8.1] · [Source: docs/DOCUMENTO-BASE.md#8.13 / ADR-008]
- [Source: docs/specs/spec-hotel-booking-hub/decisions-adr.md#ADR-008 / #ADR-019 / #ADR-020]
- [Source: deploy/dapr/pubsub.yaml, statestore.yaml] — nombres/estructura de componentes Dapr.
- [Source: .github/workflows/ci.yml] — dónde añadir el job terraform.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Amelia / dev-story, modo autónomo)

### Debug Log References

- Gate de IaC (no aplica TDD/red-green): `terraform init -backend=false` + `terraform fmt -check -recursive` + `terraform validate` → **Success** (0 warnings tras cambiar `enable_rbac_authorization`→`rbac_authorization_enabled`). Añadido como job de CI.
- `.NET` intacto (0 archivos de código tocados): build `Build succeeded` 0 warnings/0 errors; `dotnet format` limpio; suite de 450 tests sin cambios (verde en PR#24).

### Completion Notes List

- **IaC Terraform completa** de la topología Azure (ADR-008): RG + Log Analytics/App Insights + ACR + Managed Identity + SQL ×2 + Redis + Service Bus (+topic) + Key Vault + Container App Environment (Dapr/KEDA) + 4 Container Apps + componentes Dapr `pubsub`(Service Bus)/`statestore`(Redis).
- **Cero secretos** (ADR-020): contraseñas por `random_password`, secretos en Key Vault, apps passwordless por Managed Identity. gitleaks seguirá verde.
- **Cloud-agnostic por Dapr** (AC-E8.1.2): component `pubsub` = Service Bus con el mismo nombre que el RabbitMQ local; el salto local↔nube es el adaptador por entorno (Strategy, ADR-019/020), el dominio no cambia.
- **Compuerta honesta:** `plan`/`apply` requieren auth de Azure → NO ejecutados; el gate reproducible es `fmt`+`validate` (local + CI). Backend remoto documentado, no configurado. El adaptador `.NET PublicadorEventosDapr` queda documentado (requiere Dapr runtime, solo ACA) — `deferred-work.md`.

### File List

**Nuevos**
- `deploy/terraform/versions.tf`, `providers.tf`, `variables.tf`, `main.tf`, `observability.tf`, `registry.tf`, `data.tf`, `keyvault.tf`, `apps.tf`, `outputs.tf`, `README.md`

**Modificados**
- `.github/workflows/ci.yml` (job `terraform` fmt+validate)
- `docs/implementation-artifacts/deferred-work.md` (adaptador Dapr .NET de nube)

**Eliminados**
- `deploy/terraform/.gitkeep` (reemplazado por los `.tf` reales)

## Change Log

| Fecha | Cambio |
|---|---|
| 2026-07-10 | Story 8.1: IaC Terraform del despliegue a Azure (ACA+Dapr/KEDA, SQL×2, Redis, Service Bus, Key Vault, App Insights, ACR, Managed Identity) — ADR-008. Cero secretos (random_password + Key Vault + passwordless). Componentes Dapr pubsub/statestore. Gate `fmt`+`validate` (local + job de CI); `plan`/`apply` bajo compuerta de Fase 3. Status → review. |
