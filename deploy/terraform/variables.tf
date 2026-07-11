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
  description = "Región de Azure."
  type        = string
  default     = "eastus2"
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
