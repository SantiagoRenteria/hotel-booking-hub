# Datos y mensajería: una BD SQL por BC (ADR-001), Redis (caché/idempotencia/state Dapr) y Service Bus
# (transporte del pub/sub Dapr en nube, ADR-019). Las cadenas/contraseñas van a Key Vault (keyvault.tf), no
# como outputs planos.

resource "azurerm_mssql_server" "principal" {
  name                         = "${local.nombre}-sql"
  location                     = azurerm_resource_group.principal.location
  resource_group_name          = azurerm_resource_group.principal.name
  version                      = "12.0"
  administrator_login          = var.sql_admin_login
  administrator_login_password = random_password.sql_admin.result
  minimum_tls_version          = "1.2"
  # Endpoint público habilitado + firewall "Allow Azure services" para que las Container Apps (IPs de salida
  # dinámicas) alcancen la BD. Hardening de prod: private endpoint + VNet integration (documentado, no aplicado).
  public_network_access_enabled = true
  tags                          = local.tags

  # Admin AAD del servidor = el deployer (identidad de la sesión az/OIDC que corre Terraform). Habilita aplicar
  # las migraciones EF Core por token AAD (sqlcmd -G), sin contraseña SQL en el pipeline (ADR-021/022).
  azuread_administrator {
    login_username = var.sql_aad_admin_login
    object_id      = var.sql_aad_admin_object_id != "" ? var.sql_aad_admin_object_id : data.azurerm_client_config.actual.object_id
  }
}

# Regla de firewall para la IP pública del deployer (la máquina/runner que aplica migraciones). La regla
# "AllowAzureServices" (0.0.0.0) solo cubre servicios de Azure, NO la IP local → sin esta regla, `sqlcmd` desde
# la máquina del deployer da timeout. Se crea solo si se pasa `ip_deployer` (se detecta en el runbook/pipeline).
resource "azurerm_mssql_firewall_rule" "deployer" {
  count            = var.ip_deployer != "" ? 1 : 0
  name             = "AllowDeployer"
  server_id        = azurerm_mssql_server.principal.id
  start_ip_address = var.ip_deployer
  end_ip_address   = var.ip_deployer
}

# Permite que servicios de Azure (incluidas las Container Apps) se conecten al servidor SQL. La regla 0.0.0.0
# es la convención de Azure para "Allow Azure services", no un rango público real.
resource "azurerm_mssql_firewall_rule" "azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.principal.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# Una base por servicio (ADR-001): Hoteles y Reservas. GP_S serverless con auto-pause (ADR-022): cuando no hay
# conexiones la BD se pausa a 0 de cómputo (solo se paga almacenamiento marginal) → mínimo costo en el ciclo
# apply→probar→destroy. min_capacity 0.5 vCore; auto_pause_delay 60 min (mínimo permitido por Azure).
resource "azurerm_mssql_database" "hoteles" {
  name                        = "db-hoteles"
  server_id                   = azurerm_mssql_server.principal.id
  sku_name                    = "GP_S_Gen5_1"
  min_capacity                = 0.5
  auto_pause_delay_in_minutes = 60
  tags                        = local.tags
}

resource "azurerm_mssql_database" "reservas" {
  name                        = "db-reservas"
  server_id                   = azurerm_mssql_server.principal.id
  sku_name                    = "GP_S_Gen5_1"
  min_capacity                = 0.5
  auto_pause_delay_in_minutes = 60
  tags                        = local.tags
}

resource "azurerm_redis_cache" "principal" {
  name                 = "${local.nombre}-redis"
  location             = azurerm_resource_group.principal.location
  resource_group_name  = azurerm_resource_group.principal.name
  capacity             = 0
  family               = "C"
  sku_name             = "Basic"
  non_ssl_port_enabled = false
  minimum_tls_version  = "1.2"
  tags                 = local.tags
}

# Nota: el nombre de un Service Bus namespace NO puede terminar en "-sb" ni "-mgmt" (regla del provider,
# validada en `plan`, no en `validate`) → se usa "-bus" en vez de "-sb".
resource "azurerm_servicebus_namespace" "principal" {
  name                = "${local.nombre}-bus"
  location            = azurerm_resource_group.principal.location
  resource_group_name = azurerm_resource_group.principal.name
  sku                 = "Standard"
  tags                = local.tags
}

# Topic del transporte de eventos (el component Dapr `pubsub` publica aquí; ver apps.tf).
resource "azurerm_servicebus_topic" "eventos" {
  name         = "hotelbookinghub-eventos"
  namespace_id = azurerm_servicebus_namespace.principal.id
}

# Regla de autorización para que Dapr acceda al namespace (cadena de conexión → Key Vault).
resource "azurerm_servicebus_namespace_authorization_rule" "dapr" {
  name         = "dapr"
  namespace_id = azurerm_servicebus_namespace.principal.id
  listen       = true
  send         = true
  # Dapr gestiona entidades (topics/subscriptions) en runtime → requiere Manage. En prod endurecido se
  # pre-crean las entidades y se usa `disableEntityManagement=true` con permisos mínimos (documentado).
  manage = true
}
