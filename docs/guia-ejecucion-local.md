# Guía de ejecución local — paso a paso

> Guía práctica para levantar `hotel-booking-hub` en tu máquina y ejercer la API de punta a punta.
> Pensada para que **cualquier persona** lo reproduzca sin conocer el proyecto: dice **desde qué consola**
> y **desde qué carpeta** se corre cada comando, y explica los errores típicos.

## Regla de oro (lee esto primero)

**TODOS los comandos de esta guía se ejecutan desde la RAÍZ del repositorio** — la carpeta `hotel-booking-hub/`
(la que contiene `README.md`, `deploy/`, `postman/`, `src/`). No hace falta entrar a ninguna subcarpeta: **cada
comando ya trae dentro la ruta exacta de lo que necesita** (`deploy/.env`, `deploy/scripts/...`, `postman/...`).
Tú solo tienes que estar parado en la raíz.

### Cómo plantarte en la raíz (hagas lo que hagas antes)

Copia la ruta de TU repo una vez y úsala siempre para posicionarte, sin importar en qué carpeta estabas:

**🐚 Git Bash:**
```bash
cd ~/Documents/Projects/prueba_tecnica_ultragroup/hotel-booking-hub
```
**🔷 PowerShell:**
```powershell
cd C:\Users\santiago\Documents\Projects\prueba_tecnica_ultragroup\hotel-booking-hub
```

Verifica que estás bien (debe listar `README.md`, `deploy`, `postman`, `src`…):
```bash
ls
```

> **Nota:** `.env` empieza con punto → es un **archivo oculto**, y `ls` normal **no lo muestra** (eso es normal,
> no significa que falte). Para verlo usa `ls -a`.

> El tropiezo #1 es correr un comando desde `deploy/`: como el comando ya lleva `deploy/` dentro, la ruta queda
> `deploy/deploy/.env`, no existe, la clave sale vacía → todo da 401. Regla simple: **si algo falla, vuelve a
> plantarte en la raíz con el `cd` de arriba y repite.** Nunca mezcles "estar en `deploy/`" con rutas que ya dicen `deploy/`.

## ¿Qué consola uso? (importante en Windows)

Este proyecto se desarrolló en Windows. Hay tres consolas y **no son intercambiables**:

| Consola | Cuándo usarla aquí |
|---------|--------------------|
| **🐚 Git Bash** (MINGW64) | Scripts `.sh` (`mint-jwt.sh`, `smoke.sh`) — usan `openssl` y sintaxis Unix. **Solo funcionan aquí.** Es la consola recomendada para seguir esta guía. |
| **🔷 PowerShell** | Alternativa nativa si no quieres Git Bash: se mintea el token con `HMACSHA256` de .NET. Ojo: `curl` en PowerShell **no** es el curl de Unix (es un alias de `Invoke-WebRequest`) → usa `Invoke-RestMethod`. |
| **⬛ CMD** | Solo para `docker compose` y `newman`. No corre los `.sh` ni el minteo nativo. |

`docker compose` funciona igual en las tres. Cada bloque de abajo lleva una etiqueta con la consola que aplica.

---

## 1. Requisitos previos

- **Docker Desktop** corriendo (única dependencia obligatoria — no necesitas instalar .NET ni SQL).
- **Git Bash** (viene con [Git para Windows](https://git-scm.com/download/win)) — recomendado.
- **Node + Newman** *(opcional)* solo si vas a correr la colección Postman por consola: `npm install -g newman`.

### Configurar el archivo de entorno (una vez)

El stack no trae secretos en el repo. Crea tu `.env` a partir del ejemplo y define dos valores:

**🐚 Git Bash / ⬛ CMD / 🔷 PowerShell** (desde la raíz):
```bash
cp deploy/.env.example deploy/.env      # en PowerShell: Copy-Item deploy/.env.example deploy/.env
```

Edita `deploy/.env` y define:
```
MSSQL_SA_PASSWORD=Una_Clave_Fuerte123!      # contraseña de SQL Server (mínimo 8, con mayús/minús/número/símbolo)
JWT_SIGNING_KEY=<64 caracteres aleatorios>  # clave de firma de los JWT (usa exactamente 64 chars)
```

> `JWT_SIGNING_KEY` debe tener **64 caracteres**. Genera uno así:
> **🐚 Git Bash:** `openssl rand -hex 32`
> **🔷 PowerShell:** `-join ((48..57)+(97..122) | Get-Random -Count 64 | %{[char]$_})`

---

## 2. Levantar el stack

**🐳 cualquier consola** (desde la raíz):
```bash
docker compose -f deploy/docker-compose.yml up -d --build
```

Esto levanta: Gateway + Hoteles + Reservas + Worker de Notificaciones + SQL×2 + Redis + RabbitMQ + dashboard OTel.
Las migraciones de base de datos se aplican **solas** al arrancar. Espera ~20–40s la primera vez.

### URLs

| Qué | URL |
|-----|-----|
| **Gateway** (toda la API entra por aquí, con JWT) | http://localhost:8080 |
| Health del Gateway (anónimo) | http://localhost:8080/health |
| UI Scalar — Hoteles (OpenAPI navegable) | http://localhost:8081/scalar |
| UI Scalar — Reservas | http://localhost:8082/scalar |
| RabbitMQ (management UI, `guest`/`guest`) | http://localhost:15672 |
| Dashboard OTel (trazas y métricas) | http://localhost:18888 |

### Verificar que está arriba

**🐚 Git Bash:**
```bash
curl -s -o /dev/null -w "gateway -> HTTP %{http_code}\n" http://localhost:8080/health
```
**🔷 PowerShell:**
```powershell
(Invoke-WebRequest http://localhost:8080/health -UseBasicParsing).StatusCode   # 200 = OK
```

`HTTP 200` = listo. `HTTP 000` / "unable to connect" = el stack aún no arrancó o se cayó (revisa `docker compose ... ps`).

---

## 3. Cómo funciona la identidad (para entender los tokens)

**No hay tabla de usuarios.** La identidad viaja dentro del **JWT** (patrón de identidad externalizada). Cada token
lleva dos datos que lo deciden todo:

- **`role`** — qué puede hacer: `Agente` (gestiona catálogo y sus reservas) o `Viajero` (busca y reserva).
- **`email`** — **quién es**: distingue a un agente de otro. **Tú lo eliges al generar el token.**

Consecuencias:
- El `email` que pongas ES el agente. **Mismo email → mismo agente** (ve su mismo catálogo). **Email distinto → agente distinto** (catálogo aislado).
- Un agente **solo ve/edita lo suyo**: al crear un hotel, el sistema le pone como propietario el `email` del token (no se puede falsificar). Al leer, filtra por ese mismo `email`.
- Puedes inventar cualquier email — no tiene que "existir" en ningún lado. El servidor solo valida la **firma** del token (hecha con `JWT_SIGNING_KEY`), el issuer, el audience y la expiración.

En producción real, ese `email`/`role` los emitiría un proveedor de identidad (Azure AD B2C, Auth0…) tras el login.
Aquí los generamos a mano para poder actuar como cualquier agente sin montar un IdP.

---

## 4. Obtener el token JWT

Elige **una** de las dos vías según tu consola. En ambas, la clave sale de `deploy/.env` y **debe medir 64**.

### Vía A — 🐚 Git Bash (con el script del repo)

```bash
KEY="$(grep '^JWT_SIGNING_KEY=' deploy/.env | cut -d= -f2-)"
echo "len KEY: ${#KEY}"     # DEBE dar 64; si da 0 → no estás en la raíz del repo

# Token de Agente (gestiona catálogo)
TOKEN=$(bash deploy/scripts/mint-jwt.sh "$KEY" Agente  hotel-booking-hub hotel-booking-hub-api agente1@x.com)
# Token de Viajero (busca y reserva)
TOKEN_VIAJERO=$(bash deploy/scripts/mint-jwt.sh "$KEY" Viajero hotel-booking-hub hotel-booking-hub-api viajero@x.com)

# Imprime cada token SOLO en su propia línea (para copiarlo limpio, sin espacios de relleno):
echo "-- TOKEN AGENTE --";  echo "$TOKEN"
echo "-- TOKEN VIAJERO --"; echo "$TOKEN_VIAJERO"
```
Firma del script: `mint-jwt.sh "<clave>" [rol] [issuer] [audience] [email]`. El issuer/audience deben ser
exactamente `hotel-booking-hub` / `hotel-booking-hub-api` (los que valida el sistema).

> ⚠️ **Al copiar el token para pegarlo (Scalar/Postman), no incluyas espacios antes ni después.** Un espacio de
> más deja el header como `Bearer  eyJ...` y el sistema responde **401**. Por eso el token se imprime solo en su
> línea (arriba): selecciónala completa. Si lo usas por `curl`/`Invoke-RestMethod` con la variable `$TOKEN`, no hay
> riesgo — el problema solo aparece al copiar-pegar a mano.

### Vía B — 🔷 PowerShell (nativo, sin scripts .sh)

```powershell
$key = ((Select-String -Path deploy\.env -Pattern '^JWT_SIGNING_KEY=').Line) -replace '^JWT_SIGNING_KEY=',''
if ($key.Length -ne 64) { throw "Clave vacia/incorrecta ($($key.Length)). Corre esto desde la raiz del repo." }

function New-Jwt([string]$Role='Agente',[string]$Email='agente1@x.com'){
  $b64u = { param($b) [Convert]::ToBase64String($b).Replace('+','-').Replace('/','_').TrimEnd('=') }
  $now=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
  $payload="{""sub"":""$Email"",""email"":""$Email"",""role"":""$Role"",""iss"":""hotel-booking-hub"",""aud"":""hotel-booking-hub-api"",""iat"":$now,""nbf"":$($now-60),""exp"":$($now+3600)}"
  $h = & $b64u ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
  $p = & $b64u ([Text.Encoding]::UTF8.GetBytes($payload))
  $hmac=[System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($key))
  $sig = & $b64u ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$h.$p")))
  "$h.$p.$sig"
}

$TOKEN = New-Jwt Agente  'agente1@x.com'
$TOKEN_VIAJERO = New-Jwt Viajero 'viajero@x.com'
```

> Los tokens duran **1 hora**. Si expiran, vuelve a generarlos.
> El token queda guardado en la variable (`$TOKEN` / `$TOKEN_VIAJERO`) de **esa misma terminal** para reusarlo.

### Vía C — sin consola: pégalo en la UI Scalar

Abre http://localhost:8081/scalar, botón **Authorize** → pega el token (esquema Bearer) → pruebas desde el navegador.
(Necesitas el token de la Vía A o B; Scalar no lo genera.)

---

## 5. Probar la API

Con el token en la variable de la terminal:

### 🐚 Git Bash (curl real)
```bash
# Listar hoteles (paginado): sobre { items, page, pageSize, total }
curl -s -H "Authorization: Bearer $TOKEN" "http://localhost:8080/api/v1/hoteles?page=1&pageSize=20"; echo

# Crear un hotel...
curl -s -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"nombre":"Hotel Demo","ciudad":"Bogota","direccion":"Cll 1","descripcion":"prueba","estado":1}' \
  http://localhost:8080/api/v1/hoteles; echo

# ...y volver a listar → aparece de inmediato (el caché se invalida al crear)
curl -s -H "Authorization: Bearer $TOKEN" "http://localhost:8080/api/v1/hoteles?page=1&pageSize=20"; echo

# Disponibilidad (rol Viajero)
curl -s -H "Authorization: Bearer $TOKEN_VIAJERO" \
  "http://localhost:8080/api/v1/habitaciones/disponibles?ciudad=Bogota&entrada=2026-08-01&salida=2026-08-03&huespedes=2"; echo
```

### 🔷 PowerShell (Invoke-RestMethod — NO uses `curl`)
```powershell
$h = @{ Authorization = "Bearer $TOKEN" }

# Listar (devuelve objeto ya parseado)
Invoke-RestMethod -Uri "http://localhost:8080/api/v1/hoteles?page=1&pageSize=20" -Headers $h

# Crear
$body = '{"nombre":"Hotel Demo","ciudad":"Bogota","direccion":"Cll 1","descripcion":"prueba","estado":1}'
Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/v1/hoteles" -Headers $h -ContentType 'application/json' -Body $body
```

### Demostrar el aislamiento entre agentes

Genera dos tokens con **emails distintos** (Vía A/B, `agente1@x.com` y `agente2@x.com`). Crea un hotel con el
primero; al listar con el segundo **no aparece**. Esa es la separación agente↔agente, sin tabla de usuarios.

---

## 6. Reglas de la API que conviene saber

- **`rowVersion` (concurrencia optimista):** solo se envía al **modificar** algo que ya existe (editar/eliminar/
  habilitar/deshabilitar). NO al **crear** (no hay versión previa) ni al **leer** (pero el GET te lo devuelve).
  Flujo natural: `GET` un hotel → tomas su `rowVersion` → lo mandas en el `PUT`. Si mandas uno viejo → **409**.
- **Paginación:** los GET de lista (`/hoteles`, `/hoteles/{id}/habitaciones`) aceptan `?page=1&pageSize=20`.
  `pageSize` máximo **100** (pedir más → **400**); `page` mínimo **1**.
- **Unicidad de hotel:** es **por agente**. Mismo agente + mismo `(nombre, ciudad)` → **409**. Agentes distintos
  pueden tener el mismo nombre/ciudad (cada uno su catálogo) — no es un duplicado.
- **Roles:** los endpoints de gestión del catálogo son **solo Agente** (un Viajero recibe **403**); la
  disponibilidad la ven ambos.

---

## 7. Correr las pruebas automatizadas

### Colección Postman (Newman)

**🐚 Git Bash** (desde la raíz):
```bash
JWT="$(grep '^JWT_SIGNING_KEY=' deploy/.env | cut -d= -f2-)"
newman run postman/hotel-booking-hub.postman_collection.json \
  --env-var "baseUrl=http://localhost:8080" --env-var "jwtSigningKey=$JWT"
```
**🔷 PowerShell** (desde la raíz):
```powershell
$jwt = ((Select-String -Path deploy\.env -Pattern '^JWT_SIGNING_KEY=').Line) -replace '^JWT_SIGNING_KEY=',''
newman run postman/hotel-booking-hub.postman_collection.json `
  --env-var "baseUrl=http://localhost:8080" --env-var "jwtSigningKey=$jwt"
```
La colección mintea sus propios tokens (con `jwtSigningKey`) y usa un **agente único por corrida**, así que puedes
re-ejecutarla sin choques. Para probar la re-ejecutabilidad, añade `-n 2` (dos iteraciones seguidas).

#### ⚠️ Usar la colección en la APP de Postman (no en Newman) — paso OBLIGATORIO

En Newman le pasas la clave con `--env-var "jwtSigningKey=..."`. **La app de Postman NO tiene `--env-var`**, así que
tienes que ponerle la clave a mano, o **todo dará 401** aunque el token "se vea" en la variable:

1. Colección **hotel-booking-hub** → **⋯ → Edit** → pestaña **Variables**.
2. Fila **`jwtSigningKey`** → pega los **64 caracteres** de `JWT_SIGNING_KEY` (el de `deploy/.env`) en la columna
   **Current value** (⚠️ **no** en *Initial value* — ver abajo).
3. **Save** y envía cualquier request.

**Cómo funciona (léelo, evita horas de confusión):** el **pre-request a nivel de colección re-mintea `jwtAgente`
y `jwtViajero` en CADA envío**, firmándolos con `jwtSigningKey`. No es un token guardado que puedas fijar: si
escribes un token a mano en `jwtAgente`, el pre-request **lo sobreescribe** en el siguiente Send. Por eso:

- Si `jwtSigningKey` está **vacía** (default de la colección) → el token se firma con clave vacía → **401**, aunque
  el *tooltip* de `{{jwtAgente}}` muestre un token de aspecto normal (ese es un valor viejo, no el que se envía).
- **Pegar el token directo en el header sí funciona** — porque esquivas el pre-request. Pero es un parche; la forma
  correcta es setear `jwtSigningKey` y dejar que la colección lo mintee sola.

**Verifícalo:** abre la **Postman Console** (abajo a la izquierda, o `View → Show Postman Console`) y envía. Verás el
header `Authorization` **realmente enviado** — con `jwtSigningKey` vacía, ese token difiere del del tooltip y falla.

**Initial value vs Current value (trampa clásica de Postman):** *Initial value* es el que se **exporta/comparte** (por
eso en el repo va vacío — no se commitean secretos). *Current value* es **local y es el que Postman usa** en tus
envíos. Pon la clave en **Current value**; si la pones solo en *Initial*, no se aplica y seguirás con 401.

### Smoke end-to-end (solo 🐚 Git Bash — usa `openssl`)

Ejerce **todo** el flujo por el Gateway con ambos roles y casos negativos (401/403/404/409). Desde la raíz:
```bash
JWT="$(grep '^JWT_SIGNING_KEY=' deploy/.env | cut -d= -f2-)"
GATEWAY=http://localhost:8080 JWT_SIGNING_KEY="$JWT" bash deploy/scripts/smoke.sh
```

### Suite de tests del código (.NET, opcional — requiere SDK .NET 10)

```bash
dotnet test HotelBookingHub.slnx -c Release
```
Usa Testcontainers (SQL/Redis/RabbitMQ reales) → necesita Docker. Nota: si el compose está arriba, un test
sensible a recursos puede fallar por contención; corre la suite con el compose abajo, o confía en el CI.

---

## 8. Apagar el stack

```bash
docker compose -f deploy/docker-compose.yml down        # detiene (conserva los datos en volúmenes)
docker compose -f deploy/docker-compose.yml down -v      # detiene y BORRA los datos (arranque limpio)
```

---

## 9. Problemas típicos (y su causa)

| Síntoma | Causa y solución |
|---------|------------------|
| `grep: deploy/.env: No such file or directory` y token vacío | Estás **dentro de `deploy/`** (u otra carpeta), no en la raíz. Vuelve a la raíz con el `cd` de la sección "Cómo plantarte en la raíz" y repite. |
| `len KEY` da **0** | Igual que arriba: la clave no se leyó porque no estás en la raíz. Plántate en la raíz y repite. |
| `ls` no muestra `.env` | Es normal: `.env` es un archivo **oculto** (empieza con punto). Usa `ls -a` para verlo. No significa que falte. |
| **401** "No autenticado" | Token vacío, mal firmado (clave incorrecta) o **expirado** (dura 1h). Regenéralo. Verifica que `KEY` mide 64. |
| **401** aun con token que "se ve bien" | Al copiar-pegar el token metiste un **espacio** al inicio/fin → `Bearer  eyJ...`. Vuelve a copiarlo sin espacios (el token se imprime solo en su línea justo para esto). |
| **401** en la **app de Postman** con `{{jwtAgente}}`, pero pegando el token sí funciona | `jwtSigningKey` está **vacía** en Postman. El pre-request re-mintea el token en cada envío con esa clave vacía → firma inválida. Setea `jwtSigningKey` (Current value) = `JWT_SIGNING_KEY` de `deploy/.env`. Ver §7 "Usar la colección en la APP de Postman". |
| **403** "Prohibido" | Estás usando un token de **Viajero** en un endpoint **solo Agente** (o el token no trae claim `email`). |
| **409** "modificado por otra operación" | `rowVersion` obsoleto: relee el recurso (GET) para tomar el `rowVersion` actual antes del PUT/DELETE. |
| **409** al crear hotel | Ese agente ya tiene un hotel con ese `(nombre, ciudad)`. Cambia el nombre o usa otro agente. |
| **400** al listar | `pageSize` > 100 o `page` < 1. |
| `HTTP 000` / "unable to connect" | El stack no está arriba. `docker compose -f deploy/docker-compose.yml ps` y vuelve a levantarlo. |
| En PowerShell, `curl` se comporta raro | `curl` ahí es alias de `Invoke-WebRequest`. Usa `Invoke-RestMethod`, o llama `curl.exe` explícito. |
| `mint-jwt.sh: No such file or directory` en CMD/PowerShell | Los `.sh` **solo** corren en Git Bash. Usa la Vía B (PowerShell nativo) o abre Git Bash. |
