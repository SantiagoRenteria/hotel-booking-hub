# Provider de Azure. La autenticación es por variables de entorno / `az login` / OIDC en CI (NUNCA credenciales
# en el código, ADR-020). `features {}` es obligatorio aunque esté vacío.
provider "azurerm" {
  features {}
}
