# Registro de contenedores (ACR) + identidad administrada de las apps. La Managed Identity es "passwordless":
# hace pull de ACR y lee secretos de Key Vault sin credenciales en configuración (ADR-020).

resource "azurerm_container_registry" "principal" {
  name                = replace("${local.nombre}acr", "-", "")
  location            = azurerm_resource_group.principal.location
  resource_group_name = azurerm_resource_group.principal.name
  sku                 = "Basic"
  admin_enabled       = false
  tags                = local.tags
}

resource "azurerm_user_assigned_identity" "apps" {
  name                = "${local.nombre}-id"
  location            = azurerm_resource_group.principal.location
  resource_group_name = azurerm_resource_group.principal.name
  tags                = local.tags
}

# La identidad puede hacer pull de imágenes del ACR.
resource "azurerm_role_assignment" "acr_pull" {
  scope                = azurerm_container_registry.principal.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.apps.principal_id
}
