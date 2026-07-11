# Salidas útiles para el despliegue/verificación. Los secretos NO se exponen como outputs (viven en Key Vault).

output "grupo_recursos" {
  description = "Nombre del grupo de recursos."
  value       = azurerm_resource_group.principal.name
}

output "gateway_fqdn" {
  description = "FQDN público del API Gateway (único ingress externo)."
  value       = azurerm_container_app.gateway.latest_revision_fqdn
}

output "acr_login_server" {
  description = "Login server del Azure Container Registry (destino del push de imágenes)."
  value       = azurerm_container_registry.principal.login_server
}

output "key_vault_uri" {
  description = "URI del Key Vault (custodia de secretos)."
  value       = azurerm_key_vault.principal.vault_uri
}

output "app_insights_connection_string" {
  description = "Cadena de conexión de Application Insights (telemetría)."
  value       = azurerm_application_insights.principal.connection_string
  sensitive   = true
}
