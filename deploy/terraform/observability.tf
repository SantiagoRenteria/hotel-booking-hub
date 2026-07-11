# Observabilidad (NFR-5): Log Analytics + Application Insights. El Container App Environment enruta sus logs aquí
# y las apps exportan OTel a App Insights (equivalente nube del dashboard de Aspire local).

resource "azurerm_log_analytics_workspace" "principal" {
  name                = "${local.nombre}-log"
  location            = azurerm_resource_group.principal.location
  resource_group_name = azurerm_resource_group.principal.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = local.tags
}

resource "azurerm_application_insights" "principal" {
  name                = "${local.nombre}-appi"
  location            = azurerm_resource_group.principal.location
  resource_group_name = azurerm_resource_group.principal.name
  workspace_id        = azurerm_log_analytics_workspace.principal.id
  application_type    = "web"
  tags                = local.tags
}
