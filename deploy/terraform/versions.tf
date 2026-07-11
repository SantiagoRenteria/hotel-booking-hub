# Story 8.1/8.2 (ADR-008/022) — versiones fijadas de Terraform y providers + backend remoto.
#
# BACKEND remoto azurerm con auth AAD (cero claves de storage, ADR-022). Config PARCIAL: los nombres del
# RG-state/Storage/container/key se pasan en `terraform init -backend-config=...` (ver deploy/terraform/bootstrap/).
# El RG-state es permanente (no se destruye con el RG-app). CI valida con `init -backend=false` (ignora este bloque).
terraform {
  required_version = ">= 1.9.0"

  backend "azurerm" {
    use_azuread_auth = true
  }

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
