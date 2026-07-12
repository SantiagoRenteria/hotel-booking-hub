# Recursos base: grupo de recursos, naming/tags comunes, identidad del tenant y secretos generados.

data "azurerm_client_config" "actual" {}

locals {
  # Prefijo estable para todos los recursos: p. ej. "hbh-dev".
  nombre = "${var.prefijo}-${var.entorno}"
  tags   = merge(var.etiquetas, { entorno = var.entorno })

  # Cadena StackExchange.Redis para Azure Managed Redis (host:port TLS + clave). Se reutiliza en las cadenas de
  # conexión de las apps (apps.tf) y en el secreto de Key Vault (keyvault.tf).
  redis_cs = "${azurerm_managed_redis.principal.hostname}:${azurerm_managed_redis.principal.default_database[0].port},password=${azurerm_managed_redis.principal.default_database[0].primary_access_key},ssl=True,abortConnect=False"
}

resource "azurerm_resource_group" "principal" {
  name     = "${local.nombre}-rg"
  location = var.ubicacion
  tags     = local.tags
}

# Contraseña del administrador SQL generada (NUNCA hardcodeada): se guarda en Key Vault (keyvault.tf).
resource "random_password" "sql_admin" {
  length           = 24
  special          = true
  override_special = "!#$%*-_"
}

# Clave de firma JWT (HMAC) generada: en local va por env var; en nube, en Key Vault (ADR-020).
resource "random_password" "jwt" {
  length  = 48
  special = false
}
