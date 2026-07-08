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

## 3. Salto asíncrono por Dapr — estado 🛰️

Durante la Story 1.1 se **validó (de-risking)** que el salto asíncrono real funciona: se cableó Dapr pub/sub y se verificó **publish → consume cruzando el borde de proceso** (Reservas.Api → Notificaciones.Worker) sobre Redis, con Dapr CLI 1.18. Ese endpoint de **humo temporal se retiró** para no dejar mocks en el código.

El **flujo reproducible de pub/sub** (con el evento real `ReservaConfirmada`, el outbox transaccional y la suscripción idempotente en el Worker) llega en la **Story 1.6b / Épica 5**. Cuando esté, esta sección tendrá los pasos de `dapr run` end-to-end.

**Trampas de Dapr en Windows** (para tenerlas listas):

- `dapr init` (modo Docker) puede fallar con *"port 6060 is not available"* — el *scheduler* cae en un rango de puertos reservado por Windows (WSL/Hyper-V). Usa **`dapr init --slim`** (binarios locales, sin contenedores) o una terminal **como Administrador**. En slim verás errores de *placement/scheduler* en el log: **son inofensivos para pub/sub**.
- Al correr un servicio bajo Dapr con `dotnet run`, usa **`--no-launch-profile`**, o `launchSettings.json` pisa el puerto y el sidecar no alcanza la app.

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
