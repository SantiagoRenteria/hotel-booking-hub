# Story 8.1/8.2 (ADR-008/022) — versiones fijadas de Terraform y providers + backend remoto.
#
# BACKEND remoto azurerm (ADR-022). Config PARCIAL: los nombres del RG-state/Storage/container/key se pasan en
# `terraform init -backend-config=...` (ver deploy/terraform/bootstrap/). La autenticación al backend usa la
# CLAVE de la cuenta de Storage vía la variable de entorno ARM_ACCESS_KEY, obtenida en runtime con `az` (NUNCA
# en el repo → cero-secretos-en-repo intacto). Se eligió clave en vez de auth AAD porque la cuenta de despliegue
# es invitada (#EXT#) en el tenant y `az role assignment create` falla (MissingSubscription) → no se puede
# asignar el rol de datos de blob. El RG-state es permanente. CI valida con `init -backend=false` (ignora esto).
terraform {
  required_version = ">= 1.9.0"

  backend "azurerm" {}

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
