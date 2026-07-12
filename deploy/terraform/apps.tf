# Azure Container Apps: entorno (Dapr + KEDA gestionados, ADR-008), componentes Dapr por entorno (mismos nombres
# que los locales `deploy/dapr/*.yaml` → mismo contrato, distinto backend: AC-E8.1.2) y las 4 apps del sistema.

resource "azurerm_container_app_environment" "principal" {
  name                       = "${local.nombre}-cae"
  location                   = azurerm_resource_group.principal.location
  resource_group_name        = azurerm_resource_group.principal.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.principal.id
  tags                       = local.tags
}

# Component Dapr `pubsub` → Azure Service Bus. Mismo `name: pubsub` que el YAML local (RabbitMQ): cambiar de
# broker es SOLO este component, sin tocar el código (AC-E8.1.2). Scopeado a productores + consumidor.
resource "azurerm_container_app_environment_dapr_component" "pubsub" {
  name                         = "pubsub"
  container_app_environment_id = azurerm_container_app_environment.principal.id
  component_type               = "pubsub.azure.servicebus.topics"
  version                      = "v1"
  scopes                       = ["reservas", "hoteles", "notificaciones"]

  secret {
    name  = "sb-connection"
    value = azurerm_servicebus_namespace_authorization_rule.dapr.primary_connection_string
  }

  metadata {
    name        = "connectionString"
    secret_name = "sb-connection"
  }
}

# Component Dapr `statestore` → Redis (inbox de idempotencia / state), paralelo al YAML local.
resource "azurerm_container_app_environment_dapr_component" "statestore" {
  name                         = "statestore"
  container_app_environment_id = azurerm_container_app_environment.principal.id
  component_type               = "state.redis"
  version                      = "v1"
  scopes                       = ["reservas", "notificaciones"]

  # `state.redis` espera la CLAVE en redisPassword (no la cadena de conexión completa) y el host aparte.
  secret {
    name  = "redis-password"
    value = azurerm_redis_cache.principal.primary_access_key
  }

  metadata {
    name  = "redisHost"
    value = "${azurerm_redis_cache.principal.hostname}:${azurerm_redis_cache.principal.ssl_port}"
  }

  metadata {
    name        = "redisPassword"
    secret_name = "redis-password"
  }

  metadata {
    name  = "enableTLS"
    value = "true"
  }
}

# Component Dapr `secretstore` → Key Vault por Managed Identity (passwordless, ADR-020). Cumple AC-E8.1.3:
# los componentes pueden resolver secretos vía `secretKeyRef` contra este store en vez de `secret` inline.
# Migrar pubsub/statestore a `secretKeyRef` sobre este store es el endurecimiento documentado en la Nota de alcance.
resource "azurerm_container_app_environment_dapr_component" "secretstore" {
  name                         = "secretstore"
  container_app_environment_id = azurerm_container_app_environment.principal.id
  component_type               = "secretstores.azure.keyvault"
  version                      = "v1"
  scopes                       = ["reservas", "hoteles", "notificaciones"]

  metadata {
    name  = "vaultName"
    value = azurerm_key_vault.principal.name
  }

  # Identidad de usuario asignada (la misma que las apps) → acceso passwordless al Key Vault.
  metadata {
    name  = "azureClientId"
    value = azurerm_user_assigned_identity.apps.client_id
  }
}

# --- App: Gateway (único con ingress externo) ---
resource "azurerm_container_app" "gateway" {
  name                         = "${local.nombre}-gateway"
  container_app_environment_id = azurerm_container_app_environment.principal.id
  resource_group_name          = azurerm_resource_group.principal.name
  revision_mode                = "Single"
  tags                         = local.tags

  # Debe existir el rol "KV Secrets User" antes de arrancar (la app lee el secreto JWT por Managed Identity).
  depends_on = [azurerm_role_assignment.kv_lectura_apps]

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  registry {
    server   = azurerm_container_registry.principal.login_server
    identity = azurerm_user_assigned_identity.apps.id
  }

  secret {
    name                = "jwt-signing-key"
    identity            = azurerm_user_assigned_identity.apps.id
    key_vault_secret_id = azurerm_key_vault_secret.jwt.versionless_id
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"
    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  dapr {
    app_id   = "gateway"
    app_port = 8080
  }

  template {
    # Scale-to-zero (ADR-023): sin tráfico no hay réplicas → costo de cómputo ~0. Primer request paga cold start.
    min_replicas = 0
    max_replicas = 3

    container {
      name   = "gateway"
      image  = var.imagen_gateway
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      # Azure Monitor (App Insights) se alimenta por su connection string, NO por OTEL_EXPORTER_OTLP_ENDPOINT (que
      # espera una URL OTLP). En la nube, ServiceDefaults habilita el exporter Azure Monitor (UseAzureMonitor);
      # ese toggle .NET es el paso de nube diferido (ver deferred-work.md).
      env {
        name  = "APPLICATIONINSIGHTS_CONNECTION_STRING"
        value = azurerm_application_insights.principal.connection_string
      }
      env {
        name        = "Jwt__SigningKey"
        secret_name = "jwt-signing-key"
      }
      # Override del ruteo YARP para la nube: en ACA los servicios internos se resuelven por su NOMBRE de app
      # (`hbh-dev-hoteles`), no por el nombre de docker-compose (`hoteles`). Sobreescribe el Address del appsettings.
      env {
        name  = "ReverseProxy__Clusters__hoteles__Destinations__d1__Address"
        value = "http://${local.nombre}-hoteles"
      }
      env {
        name  = "ReverseProxy__Clusters__reservas__Destinations__d1__Address"
        value = "http://${local.nombre}-reservas"
      }
    }
  }
}

# --- Apps internas (hoteles, reservas, notificaciones): ingress interno del entorno ---
resource "azurerm_container_app" "hoteles" {
  name                         = "${local.nombre}-hoteles"
  container_app_environment_id = azurerm_container_app_environment.principal.id
  resource_group_name          = azurerm_resource_group.principal.name
  revision_mode                = "Single"
  tags                         = local.tags

  depends_on = [azurerm_role_assignment.kv_lectura_apps]

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  registry {
    server   = azurerm_container_registry.principal.login_server
    identity = azurerm_user_assigned_identity.apps.id
  }

  secret {
    name                = "jwt-signing-key"
    identity            = azurerm_user_assigned_identity.apps.id
    key_vault_secret_id = azurerm_key_vault_secret.jwt.versionless_id
  }

  ingress {
    external_enabled = false
    target_port      = 8080
    transport        = "auto"
    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  # Cadena de conexión a la BD (auth SQL; la contraseña viene de random_password, nunca del repo). El valor es un
  # secreto del Container App (no env plano). Connection Timeout alto por el cold start del auto-resume (GP_S).
  secret {
    name  = "cs-hotelesdb"
    value = "Server=tcp:${azurerm_mssql_server.principal.fully_qualified_domain_name},1433;Initial Catalog=db-hoteles;User ID=${var.sql_admin_login};Password=${random_password.sql_admin.result};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;"
  }

  # Managed Redis (host:port TLS + clave) para la caché de lectura del catálogo (Story T.6).
  secret {
    name  = "cs-redis"
    value = local.redis_cs
  }

  dapr {
    app_id   = "hoteles"
    app_port = 8080
  }

  template {
    # min=1 (ADR-023): servicio INTERNO detrás del gateway. ACA no despierta de forma fiable un servicio interno
    # scale-to-zero por tráfico servicio-a-servicio (el gateway ruteando da 502 mientras arranca) → se mantiene 1
    # réplica caliente. El scale-to-zero real queda en el gateway (ingress externo, sí activa).
    min_replicas = 1
    max_replicas = 3

    container {
      name   = "hoteles"
      image  = var.imagen_hoteles
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      # Azure Monitor (App Insights) se alimenta por su connection string, NO por OTEL_EXPORTER_OTLP_ENDPOINT (que
      # espera una URL OTLP). En la nube, ServiceDefaults habilita el exporter Azure Monitor (UseAzureMonitor);
      # ese toggle .NET es el paso de nube diferido (ver deferred-work.md).
      env {
        name  = "APPLICATIONINSIGHTS_CONNECTION_STRING"
        value = azurerm_application_insights.principal.connection_string
      }
      # Transporte de eventos en nube = Dapr→Service Bus (ADR-019); en local/compose no se setea → RabbitMQ.
      env {
        name  = "TransporteEventos"
        value = "Dapr"
      }
      env {
        name        = "ConnectionStrings__hotelesdb"
        secret_name = "cs-hotelesdb"
      }
      env {
        name        = "ConnectionStrings__redis"
        secret_name = "cs-redis"
      }
      env {
        name        = "Jwt__SigningKey"
        secret_name = "jwt-signing-key"
      }
    }
  }
}

resource "azurerm_container_app" "reservas" {
  name                         = "${local.nombre}-reservas"
  container_app_environment_id = azurerm_container_app_environment.principal.id
  resource_group_name          = azurerm_resource_group.principal.name
  revision_mode                = "Single"
  tags                         = local.tags

  depends_on = [azurerm_role_assignment.kv_lectura_apps]

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  registry {
    server   = azurerm_container_registry.principal.login_server
    identity = azurerm_user_assigned_identity.apps.id
  }

  secret {
    name                = "jwt-signing-key"
    identity            = azurerm_user_assigned_identity.apps.id
    key_vault_secret_id = azurerm_key_vault_secret.jwt.versionless_id
  }

  ingress {
    external_enabled = false
    target_port      = 8080
    transport        = "auto"
    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  secret {
    name  = "cs-reservasdb"
    value = "Server=tcp:${azurerm_mssql_server.principal.fully_qualified_domain_name},1433;Initial Catalog=db-reservas;User ID=${var.sql_admin_login};Password=${random_password.sql_admin.result};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;"
  }

  # Redis para caché de disponibilidad (3.2) e idempotencia de reserva (1.7). Cadena StackExchange de Azure
  # Managed Redis (host:port TLS + clave), armada en local.redis_cs.
  secret {
    name  = "cs-redis"
    value = local.redis_cs
  }

  dapr {
    app_id   = "reservas"
    app_port = 8080
  }

  template {
    # min=1 (ADR-023): servicio INTERNO detrás del gateway (ver nota en hoteles).
    min_replicas = 1
    max_replicas = 5

    container {
      name   = "reservas"
      image  = var.imagen_reservas
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      # Azure Monitor (App Insights) se alimenta por su connection string, NO por OTEL_EXPORTER_OTLP_ENDPOINT (que
      # espera una URL OTLP). En la nube, ServiceDefaults habilita el exporter Azure Monitor (UseAzureMonitor);
      # ese toggle .NET es el paso de nube diferido (ver deferred-work.md).
      env {
        name  = "APPLICATIONINSIGHTS_CONNECTION_STRING"
        value = azurerm_application_insights.principal.connection_string
      }
      env {
        name  = "TransporteEventos"
        value = "Dapr"
      }
      env {
        name        = "ConnectionStrings__reservasdb"
        secret_name = "cs-reservasdb"
      }
      env {
        name        = "ConnectionStrings__redis"
        secret_name = "cs-redis"
      }
      env {
        name        = "Jwt__SigningKey"
        secret_name = "jwt-signing-key"
      }
    }
  }
}

resource "azurerm_container_app" "notificaciones" {
  name                         = "${local.nombre}-notificaciones"
  container_app_environment_id = azurerm_container_app_environment.principal.id
  resource_group_name          = azurerm_resource_group.principal.name
  revision_mode                = "Single"
  tags                         = local.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  registry {
    server   = azurerm_container_registry.principal.login_server
    identity = azurerm_user_assigned_identity.apps.id
  }

  # Redis para el inbox de idempotencia del worker (dedup entre instancias; sin él, fallback en memoria).
  secret {
    name  = "cs-redis"
    value = local.redis_cs
  }

  dapr {
    app_id   = "notificaciones"
    app_port = 8080
  }

  template {
    # NO scale-to-zero (ADR-023): el worker consume del Service Bus; con min=0 no habría quién procese salvo un
    # KEDA scaler azure-servicebus, que en ACA reintroduce un secreto de conexión (choca con cero-secretos).
    # min=1 (cómputo ínfimo en Consumption) garantiza el consumo end-to-end. Scaler por workload identity = deuda.
    min_replicas = 1
    max_replicas = 3

    container {
      name   = "notificaciones"
      image  = var.imagen_notificaciones
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      # Azure Monitor (App Insights) se alimenta por su connection string, NO por OTEL_EXPORTER_OTLP_ENDPOINT (que
      # espera una URL OTLP). En la nube, ServiceDefaults habilita el exporter Azure Monitor (UseAzureMonitor);
      # ese toggle .NET es el paso de nube diferido (ver deferred-work.md).
      env {
        name  = "APPLICATIONINSIGHTS_CONNECTION_STRING"
        value = azurerm_application_insights.principal.connection_string
      }
      env {
        name        = "ConnectionStrings__redis"
        secret_name = "cs-redis"
      }
      # Suscripción Dapr pub/sub (el worker recibe los eventos del Service Bus por el sidecar Dapr).
      env {
        name  = "TransporteEventos"
        value = "Dapr"
      }
    }
  }
}
