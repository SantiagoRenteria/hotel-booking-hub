# Provider de Azure. La autenticación es por variables de entorno / `az login` / OIDC en CI (NUNCA credenciales
# en el código, ADR-020). `features {}` es obligatorio aunque esté vacío.
provider "azurerm" {
  features {
    # Ciclo efímero apply→destroy: al destruir, PURGA el Key Vault en vez de dejarlo soft-deleted 7 días. Si no,
    # el siguiente deploy "recuperaría" el vault viejo (con sus secretos) → colisión "already exists". En dev no
    # hay purge protection, así que la purga es válida. (En prod no se hace teardown efímero.)
    key_vault {
      purge_soft_delete_on_destroy = true
    }
  }
}
