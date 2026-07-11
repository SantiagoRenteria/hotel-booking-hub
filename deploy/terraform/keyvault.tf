# Key Vault: custodia los secretos (contraseña SQL, cadenas de Service Bus/Redis, clave JWT). RBAC en vez de
# access policies. La Managed Identity de las apps tiene rol de lectura de secretos (passwordless, ADR-020).

resource "azurerm_key_vault" "principal" {
  name                       = "${local.nombre}-kv"
  location                   = azurerm_resource_group.principal.location
  resource_group_name        = azurerm_resource_group.principal.name
  tenant_id                  = data.azurerm_client_config.actual.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  # Purge protection en prod (evita borrado permanente de secretos, requisito de cumplimiento); relajado en dev/stg.
  purge_protection_enabled   = var.entorno == "prod"
  soft_delete_retention_days = 7
  tags                       = local.tags
}

# La identidad de las apps lee secretos; el ejecutor de Terraform (deploy) puede escribirlos.
resource "azurerm_role_assignment" "kv_lectura_apps" {
  scope                = azurerm_key_vault.principal.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.apps.principal_id
}

resource "azurerm_role_assignment" "kv_admin_deployer" {
  scope                = azurerm_key_vault.principal.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.actual.object_id
}

resource "azurerm_key_vault_secret" "sql_password" {
  name         = "sql-admin-password"
  value        = random_password.sql_admin.result
  key_vault_id = azurerm_key_vault.principal.id
  depends_on   = [azurerm_role_assignment.kv_admin_deployer]
}

resource "azurerm_key_vault_secret" "jwt" {
  name         = "jwt-signing-key"
  value        = random_password.jwt.result
  key_vault_id = azurerm_key_vault.principal.id
  depends_on   = [azurerm_role_assignment.kv_admin_deployer]
}

resource "azurerm_key_vault_secret" "servicebus" {
  name         = "servicebus-connection"
  value        = azurerm_servicebus_namespace_authorization_rule.dapr.primary_connection_string
  key_vault_id = azurerm_key_vault.principal.id
  depends_on   = [azurerm_role_assignment.kv_admin_deployer]
}

resource "azurerm_key_vault_secret" "redis" {
  name         = "redis-connection"
  value        = azurerm_redis_cache.principal.primary_connection_string
  key_vault_id = azurerm_key_vault.principal.id
  depends_on   = [azurerm_role_assignment.kv_admin_deployer]
}
