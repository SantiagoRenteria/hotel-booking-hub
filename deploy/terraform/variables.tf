# Variables del módulo. Sin valores sensibles por defecto (cero secretos en el repo, ADR-020):
# la clave JWT y las contraseñas se generan (`random_password`) o se inyectan por variable en tiempo de `apply`.

variable "prefijo" {
  description = "Prefijo de nombres de recursos (marca del producto)."
  type        = string
  default     = "hbh"
}

variable "entorno" {
  description = "Entorno de despliegue (dev/stg/prod); parte del nombre de los recursos."
  type        = string
  default     = "dev"

  # El nombre del Key Vault (`${prefijo}-${entorno}-kv`) tiene un techo de 24 chars: se acota el entorno para no
  # desbordarlo en `apply` (con prefijo por defecto "hbh", entorno ≤ 17 mantiene el KV ≤ 24).
  validation {
    condition     = length(var.entorno) >= 2 && length(var.entorno) <= 12
    error_message = "El entorno debe tener entre 2 y 12 caracteres (límite de nombres de Azure, p. ej. Key Vault ≤ 24)."
  }
}

variable "ubicacion" {
  description = "Región de Azure. West US 2: la suscripción 'Estudio' permite SQL aquí (eastus2/eastus lo bloquean)."
  type        = string
  default     = "westus2"
}

# Región del Azure Managed Redis, SEPARADA de `ubicacion`: el tier Balanced NO aprovisiona en West US 2 ni West
# US 3 (Azure acepta el create y falla al provisionar → capacidad/disponibilidad regional del tier, verificado
# 2026-07-12). Sí aprovisiona en Central US (y East US 2). El resto del stack sigue en `ubicacion` (SQL solo se
# deja crear ahí); el Redis vive en Central US (latencia cross-region asumible en un entorno de prueba).
variable "ubicacion_redis" {
  description = "Región del Managed Redis (Balanced no está disponible en West US 2/3; Central US sí)."
  type        = string
  default     = "centralus"
}

variable "sql_admin_login" {
  description = "Usuario administrador del servidor SQL."
  type        = string
  default     = "hbhadmin"
}

variable "imagen_gateway" {
  description = "Imagen del API Gateway (ACR). Se sobreescribe en el pipeline de despliegue."
  type        = string
  default     = "mcr.microsoft.com/k8se/quickstart:latest"
}

variable "imagen_hoteles" {
  type    = string
  default = "mcr.microsoft.com/k8se/quickstart:latest"
}

variable "imagen_reservas" {
  type    = string
  default = "mcr.microsoft.com/k8se/quickstart:latest"
}

variable "imagen_notificaciones" {
  type    = string
  default = "mcr.microsoft.com/k8se/quickstart:latest"
}

variable "ip_deployer" {
  description = "IP pública del deployer para la regla de firewall SQL (aplicar migraciones). Vacío = no se crea la regla. Se detecta en el runbook/pipeline."
  type        = string
  default     = ""
}

variable "etiquetas" {
  description = "Tags comunes de todos los recursos."
  type        = map(string)
  default = {
    proyecto = "hotel-booking-hub"
    gestion  = "terraform"
  }
}
