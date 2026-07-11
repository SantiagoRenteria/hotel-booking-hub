# Evidencia — Story 8.2: despliegue real + smoke E2E en Azure

**Fecha:** 2026-07-11 · **Región:** West US 2 · **Suscripción:** "Subcripcion Estudio" · **Ciclo:** apply → smoke → destroy (mínimo costo)

## Resultado

Sistema desplegado por Terraform (32 recursos) y **flujo de negocio verificado end-to-end contra Azure real**:

| Paso | Endpoint | Resultado |
|---|---|---|
| Health | `GET /health` (gateway) | **200** |
| Crear hotel | `POST /api/v1/hoteles` | **201** (id `019f5174-ac6f-…`) |
| Crear habitación | `POST /api/v1/hoteles/{id}/habitaciones` | **201** (id `019f5174-b136-…`) |
| Crear reserva | `POST /api/v1/reservas` | **201** (`estado: Confirmada`, `precioTotal: 238.00` = (100+19)×2 noches) |
| Cancelar (atajo) | `POST /api/v1/reservas/{id}/cancelaciones/atajo` | **OK** (Iniciador=Agente, Decisión=AprobarAplicandoPenalidad) |

Todo autenticado con JWT (HS256, clave desde Key Vault), ruteado por el **API Gateway** (YARP) a los servicios internos, y persistido en **Azure SQL** (GP_S serverless; migraciones EF idempotentes aplicadas por auth SQL). Redis = **Azure Managed Redis** (Balanced_B0).

## Qué prueba

- **IaC ejecutable de verdad:** Terraform provisiona ACA + Dapr/KEDA + SQL×2 + Managed Redis + Service Bus + Key Vault + App Insights + ACR + Managed Identity en una suscripción real.
- **Cero secretos:** contraseñas por `random_password`, secretos en Key Vault, apps passwordless por Managed Identity; el backend del tfstate por clave de cuenta obtenida en runtime (nunca en el repo).
- **Data-plane real:** los servicios arrancan, se conectan a Azure SQL (el relay de outbox de Hoteles consulta `OutboxMessages` cada 2s) y sirven el flujo de negocio HTTP.
- **Scale-to-zero:** el gateway (ingress externo) escala a 0 y despierta por request; los servicios internos (`hoteles`/`reservas`) y el worker se mantienen en `min=1` (ver ADR-023).

## Límite conocido (documentado)

- **Evento → worker NO fluye en la nube.** El worker de Notificaciones está **desplegado y vivo** (heartbeat en logs) pero **no recibe** los eventos de reserva/cancelación: el transporte de nube (**Dapr → Service Bus**) sigue **diferido** (`PublicadorEventosDapr` no implementado; el publicador degrada a `PublicadorEventosLog`). En local el transporte SÍ corre (RabbitMQ directo, Épica 9). Cerrar el adaptador Dapr de nube es el seguimiento pendiente (`deferred-work.md`, ADR-019).

## Restricciones de la suscripción halladas (y resueltas)

1. **Azure SQL bloqueado en East US 2/East US** → región **West US 2** (verificado por capabilities API).
2. **Azure Cache for Redis clásico retirado** → **Azure Managed Redis** (`Balanced_B0`).
3. **Cuenta invitada (#EXT#) no puede asignar rol de datos** (`az role assignment create` → MissingSubscription) → backend del tfstate por **clave de cuenta** (ADR-022 ajustado).
4. **Consistencia eventual de ARM** (404 read-after-create) → **apply por fases (parents→dependientes) + reintentos**.
5. **Scale-to-zero de servicios internos** poco fiable tras el gateway → `hoteles`/`reservas` a `min=1`.
6. **Ruteo del gateway** (YARP) usaba nombres de docker-compose → override por env a los nombres de app de ACA (`hbh-dev-hoteles`).

## Teardown

`az group delete -n hbh-dev-rg --yes` — RG-app efímero eliminado; RG-state (`hbh-tfstate-rg`) permanente. Sin recursos facturables residuales.
