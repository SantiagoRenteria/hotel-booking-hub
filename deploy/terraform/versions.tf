# Story 8.1 (ADR-008) — versiones fijadas de Terraform y providers.
#
# BACKEND: para la prueba se valida con `terraform init -backend=false` (sin estado remoto). En producción
# se usaría un backend remoto con lock — descomentar y parametrizar:
#
#   backend "azurerm" {
#     resource_group_name  = "hbh-tfstate-rg"
#     storage_account_name = "hbhtfstate"
#     container_name       = "tfstate"
#     key                  = "hotel-booking-hub.tfstate"
#   }
terraform {
  required_version = ">= 1.9.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.20"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }
}
