# Cómo ejecutarlo tú mismo (runbook)

Pasos **verificados** para correr el esqueleto (Story 1.1) en tu máquina: compilar, testear, ver el **salto asíncrono real por Dapr**, y levantar todo con Docker Compose. Incluye las **trampas de Windows** que encontramos.

> Windows: los ejemplos de variables de entorno usan sintaxis **PowerShell** (`$env:X='...'`). En Git Bash sería `X=... comando`.

---

## 1. Requisitos

| Herramienta | Para qué | Notas |
|-------------|----------|-------|
| **.NET 10 SDK** | compilar, testear, `dotnet run` | `dotnet --version` → `10.0.x` |
| **Docker Desktop** | Redis/broker, `docker compose` | necesario para integración y compose |
| **Dapr CLI** (opcional) | ver el salto async fuera de compose | solo para la sección 3 |

> No necesitas instalar las plantillas de Aspire ni el *workload* de Aspire para **compilar o ejecutar** este repo — el AppHost se resuelve solo por NuGet (`Aspire.AppHost.Sdk`). Las plantillas (`dotnet new install Aspire.ProjectTemplates`) solo hacen falta si vas a **generar** proyectos nuevos.

---

## 2. Compilar y testear (sin Docker)

```powershell
dotnet build HotelBookingHub.slnx           # 0 warnings (TreatWarningsAsErrors activo)
dotnet test  HotelBookingHub.slnx           # NetArchTest: Domain no depende de EF Core
dotnet format HotelBookingHub.slnx --verify-no-changes   # estilo conforme al .editorconfig
```

Los tres deben salir en verde. Si `build` falla con `NU1903`, es un paquete con CVE: se corrige pineando la versión parcheada en `Directory.Packages.props` (ya lo hicimos con `Microsoft.OpenApi`).

---

## 3. Salto asíncrono real por Dapr (publish → consume) 🛰️

Esto reproduce lo que probamos: **Reservas.Api publica** un evento y **Notificaciones.Worker lo consume** por Dapr pub/sub.

### 3.1 Instalar el Dapr CLI

Opción rápida (sin elevar), descarga directa a `C:\dapr`:

```powershell
$tag = (Invoke-RestMethod "https://api.github.com/repos/dapr/cli/releases/latest").tag_name
Invoke-WebRequest "https://github.com/dapr/cli/releases/download/$tag/dapr_windows_amd64.zip" -OutFile "$env:TEMP\dapr.zip"
Expand-Archive "$env:TEMP\dapr.zip" -DestinationPath "C:\dapr" -Force
# Añade C:\dapr al PATH de tu sesión (o permanente):
$env:Path = "C:\dapr;$env:Path"
dapr --version
```

### 3.2 Inicializar el runtime

```powershell
dapr init --slim
```

> **Trampa de Windows:** `dapr init` (modo Docker) puede fallar con *"port 6060 is not available"* — el *scheduler* cae en un **rango de puertos reservado** por Windows (WSL/Hyper-V). Soluciones: usar **`--slim`** (lo que hacemos aquí; corre los binarios locales, sin contenedores) o abrir una terminal **como Administrador** y correr `dapr init`. En modo slim verás errores de *placement/scheduler* en el log — **son inofensivos para pub/sub**.

### 3.3 Levantar un Redis para el pub/sub

```powershell
docker run -d --name hbh-redis-dev -p 6379:6379 redis:7.4-alpine
```

El componente local ya está en `deploy/dapr/local/pubsub.yaml` (apunta a `localhost:6379`).

### 3.4 Arrancar los dos servicios con sidecar (dos terminales)

**Terminal A — Worker (suscriptor):**
```powershell
$env:ASPNETCORE_URLS='http://localhost:5081'
dapr run --app-id notificaciones --app-port 5081 --dapr-http-port 3501 --resources-path deploy/dapr/local `
  -- dotnet run --project src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones.Worker.csproj --no-launch-profile
```

**Terminal B — Reservas (publicador):**
```powershell
$env:ASPNETCORE_URLS='http://localhost:5080'
dapr run --app-id reservas --app-port 5080 --dapr-http-port 3500 --resources-path deploy/dapr/local `
  -- dotnet run --project src/Servicios/Reservas/Reservas.Api/Reservas.Api.csproj --no-launch-profile
```

> **Trampa clave:** el `--no-launch-profile` es **obligatorio**. Sin él, `dotnet run` aplica el `launchSettings.json` y la app escucha en un puerto aleatorio (p. ej. 5192) en vez del `--app-port` que le dijiste a Dapr, y el sidecar nunca la alcanza.

### 3.5 Publicar y ver el consumo

**Terminal C:**
```powershell
curl -Method POST http://localhost:5080/_smoke/ping     # PowerShell: Invoke-WebRequest
# o en Git Bash:  curl -X POST http://localhost:5080/_smoke/ping
```

En la **Terminal A** verás:
```
Worker recibió evento de humo por Dapr pub/sub: Id=019f43ef-... Type=SmokePing.v1
```

### 3.6 Apagar

```powershell
dapr stop --app-id reservas
dapr stop --app-id notificaciones
docker rm -f hbh-redis-dev
```

---

## 4. Todo el stack con Docker Compose 🐳

Reproducibilidad sin instalar el SDK (G2).

```powershell
# 1) Secreto local (NO se versiona; deploy/.env está en .gitignore)
Copy-Item deploy/.env.example deploy/.env
# edita deploy/.env y pon una contraseña que cumpla la política de SQL Server, p. ej.:
#   MSSQL_SA_PASSWORD=Hbh_Local_Dev_2026!

# 2) Levantar
docker compose -f deploy/docker-compose.yml up -d --build

# 3) Verificar salud (gateway 8080, hoteles 8081, reservas 8082, notificaciones 8083)
curl http://localhost:8080/health
curl http://localhost:8081/health
curl http://localhost:8082/health
curl http://localhost:8083/health

# 4) Apagar y limpiar
docker compose -f deploy/docker-compose.yml down -v
```

> La primera vez tarda: descarga la imagen **SDK de .NET 10** (grande) y las de **SQL Server ×2**, Redis, RabbitMQ y el dashboard de Aspire, y compila 4 imágenes. Paciencia en el primer `up --build`.

---

## 5. Resumen de trampas (para no tropezar)

1. **`dapr init` en Windows** → usa `--slim` (o terminal elevada) por el puerto 6060 reservado.
2. **`dotnet run` bajo Dapr** → siempre `--no-launch-profile`, o el puerto no coincide con `--app-port`.
3. **`docker compose`** → crea `deploy/.env` con `MSSQL_SA_PASSWORD` antes del `up` (cero secretos en el repo).
4. **`NU1903` al compilar** → es un CVE en un paquete; pinéalo parcheado en `Directory.Packages.props`.
5. **Solución** → el archivo es `HotelBookingHub.slnx` (formato XML nuevo del SDK .NET 10), no `.sln`.
