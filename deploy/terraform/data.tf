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
  tags                         = local.tags
}

# Una base por servicio (ADR-001): Hoteles y Reservas. SKU barato (Basic) — la escala a esta prueba sobra.
resource "azurerm_mssql_database" "hoteles" {
  name      = "db-hoteles"
  server_id = azurerm_mssql_server.principal.id
  sku_name  = "Basic"
  tags      = local.tags
}

resource "azurerm_mssql_database" "reservas" {
  name      = "db-reservas"
  server_id = azurerm_mssql_server.principal.id
  sku_name  = "Basic"
  tags      = local.tags
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

resource "azurerm_servicebus_namespace" "principal" {
  name                = "${local.nombre}-sb"
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
  manage       = false
}
