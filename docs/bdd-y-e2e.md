# Flujos BDD y estrategia de pruebas E2E

> Navegación: [README](../README.md) · [Uso de IA](uso-de-ia.md) · [Observabilidad](observabilidad.md) · [Épicas](planning-artifacts/epics.md) · **BDD y E2E** (este documento)

Este documento consolida **cómo se expresa el comportamiento (BDD)** en el proyecto y **cómo se verifica de extremo a extremo (E2E)**. No es un manual de testing genérico: describe las convenciones reales del código, el mapeo historia↔test, la pirámide de pruebas efectiva y las decisiones deliberadas (por qué **no** hay SpecFlow ni Playwright).

---

## 1. BDD: dónde y cómo

El comportamiento se especifica en formato **Given/When/Then (Dado/Cuando/Entonces)** en dos niveles, sin ceremonia innecesaria: se aplica donde **aporta diseño** (dominio rico, mensajería, observabilidad) y se evita donde sería teatro (CRUD directo).

### a) BDD como criterios de aceptación (en las historias)
Las historias de alta complejidad nacen con sus AC en Gherkin. El caso más explícito es la **Story 3.1** (proyección de habitaciones idempotente y ordenada), cuyos AC son bloques ` ```gherkin ` con `Feature` / `Scenario` / `Scenario Outline` — y el propio documento declara que *"cada Scenario se implementa como un test nombrado por el escenario"* ([`docs/implementation-artifacts/3-1-proyeccion-de-habitaciones-idempotente-y-ordenada.md`](implementation-artifacts/3-1-proyeccion-de-habitaciones-idempotente-y-ordenada.md)).

### b) BDD en el nombre del test (dominio de cancelación)
El ciclo de vida de cancelación —la lógica de negocio más rica— usa métodos nombrados `Dado_..._cuando_..._entonces_...`:

- [`tests/Reservas.UnitTests/Dominio/ReservaCancelacionTests.cs`](../tests/Reservas.UnitTests/Dominio/ReservaCancelacionTests.cs) — máquina de estados y congelación de penalidad (Story 4.1).
- [`tests/Reservas.UnitTests/Dominio/ReservaResolucionTests.cs`](../tests/Reservas.UnitTests/Dominio/ReservaResolucionTests.cs) — aprobar/condonar/rechazar (Story 4.2).

### c) BDD en comentarios (mensajería y observabilidad)
Donde el arrange/act/assert es más técnico, los tres momentos se marcan con comentarios `// Given / // When / // Then` para exhibir el diseño del escenario: consumidores idempotentes y dead-letter (`tests/Notificaciones.UnitTests/*`), worker bajo ráfaga + broker caído (`WorkerG3Tests`), transporte RabbitMQ real (`TransporteRabbitMqTests`), correlación de traza (`CorrelacionTrazaTests`), y el pipeline de tracing (`TracingBehaviorTests`, `TrazaPipelineTests`).

> **Nota honesta:** no se usa un runner Gherkin (SpecFlow/Reqnroll) ni existen ficheros `.feature`. El Gherkin vive como **texto de especificación en las historias**; su ejecución es un test de xUnit nombrado por el escenario. Ver §4 para el porqué.

---

## 2. Mapeo historia ↔ test BDD

Trazabilidad directa entre el comportamiento especificado y su verificación (cada clase de test abre con un `<summary>` que cita la Story y sus AC):

| Story | Comportamiento | Test(s) BDD | Nivel |
|---|---|---|---|
| **3.1** | Proyección idempotente y ordenada (eventos de catálogo v1/v2 desordenados, re-entrega) | `ProyeccionHabitacionConvergenciaTests` · `ProyeccionCatalogoTests` | unit + integración (Testcontainers) |
| **4.1** | Solicitar cancelación (transición + penalidad congelada) | `ReservaCancelacionTests` · `SolicitudCancelacionTests` | unit + integración |
| **4.2** | Resolver cancelación (aprobar/condonar/rechazar, auditoría) | `ReservaResolucionTests` · `ResolverCancelacionTests` | unit + integración |
| **4.3** | Atajo de un paso + visibilidad de pendientes | `AtajoYVisibilidadCancelacionTests` | integración |
| **5.1b** | Worker idempotente sin pérdida ni duplicado (ráfaga, broker caído) | `WorkerG3Tests` · `ConsumidorIdempotenteTests` | integración + unit |
| **9.1** | Transporte real de eventos por RabbitMQ (publica→consume→notifica, dedup por MessageId) | `TransporteRabbitMqTests` · `TransportePublicadorRabbitMqTests` | integración (Testcontainers) |
| **1.6c** | Confirmación única bajo concurrencia ("money test") | `MoneyTestG1Tests` | integración (concurrencia real) |

---

## 3. La pirámide de pruebas (verificación E2E efectiva)

El sistema es una **API headless** (sin frontend en el alcance). El rol que en una app con UI cumpliría un E2E de navegador aquí lo cubren, en capas, cuatro tipos de prueba + el smoke:

```
        ┌───────────────────────────────────────────────┐
        │  Smoke + Newman/Postman  (proceso completo)    │  ← Gateway real + 3 servicios + SQL/Redis/RabbitMQ
        ├───────────────────────────────────────────────┤
        │  Functional (WebApplicationFactory)  HTTP real │  ← borde HTTP de un servicio/Gateway, sin BD
        ├───────────────────────────────────────────────┤
        │  Integration (Testcontainers)  infra real      │  ← SQL Server / Redis / RabbitMQ reales
        ├───────────────────────────────────────────────┤
        │  Unit (xUnit)  dominio + handlers + validators  │  ← rápido, EF InMemory donde aplica
        └───────────────────────────────────────────────┘
                    Contract tests (forma de eventos)  ── transversal
```

- **Unit** (`*.UnitTests`): dominio (precio, penalidad, máquina de estados), handlers/validators CQRS, mapeo HTTP, disciplina de capas (NetArchTest), observabilidad. Framework: **xUnit** (`Directory.Packages.props`).
- **Integration** (`*.IntegrationTests`): infra real con **Testcontainers** (`Testcontainers.MsSql`/`Redis`/`RabbitMq`): anti-overbooking, outbox+relay, cancelación sobre SQL real, caché/búsqueda, inbox idempotente Redis, transporte RabbitMQ end-to-end.
- **Functional / E2E-HTTP** (`*.FunctionalTests`): borde HTTP real con **`WebApplicationFactory`** — idempotencia de reserva por header, RBAC, métricas de duración; y el borde del Gateway (`Seguridad.FunctionalTests`: autenticación 401, rate limiting, y el **ruteo de disponibilidad** hacia Reservas — `RuteoGatewayTests`, Story T.2).
- **Contract** (`tests/Contracts`): fija la **forma de los eventos de integración** (`ReservaConfirmadaV1`, etc.) para que productor y consumidor no diverjan.
- **Smoke + Newman** (data-plane completo): `docker compose up` + [`deploy/scripts/smoke.sh`](../deploy/scripts/smoke.sh) y la [colección Postman](../postman/hotel-booking-hub.postman_collection.json) por **Newman** en CI (job `smoke-compose`). Ejercen **todos** los endpoints alcanzables por el Gateway con ambos roles y casos negativos (401/403/404/409/422). El mismo smoke corre contra Azure real tras un despliegue.

### El flujo reserva → evento → notificación (partido por contrato)
No hay **un** test único que recorra "reserva HTTP → RabbitMQ → notificación" en un solo proceso, **por diseño de BC**: el write-path (`reserva → outbox`) se verifica en `Reservas.IntegrationTests` (atomicidad, relay), y el consumo (`evento → notificación`) en `Notificaciones.IntegrationTests` (`TransporteRabbitMqTests`, `WorkerG3Tests`). La unión entre ambos es el **contrato de evento compartido** (`HotelBookingHub.Comun.Eventos`), fijado por los *contract tests*. El **smoke del compose** sí ejerce el flujo completo entre procesos reales y verifica el efecto en los logs del worker.

---

## 4. Decisiones de estrategia (lo que NO se hizo, y por qué)

- **Sin SpecFlow / Reqnroll (Gherkin ejecutable).** El Gherkin se usa como *lenguaje de especificación* en las historias, materializado en tests xUnit nombrados por escenario. Montar un runner Gherkin añadiría un binding layer y `.feature` files sin aportar cobertura nueva: el valor del BDD (pensar en Given/When/Then) ya está capturado. Sería ceremonia. Las Stories 3.2 y 3.3 lo declaran explícitamente ("NO BDD/Gherkin ceremonial").
- **Sin Playwright / Cypress (E2E de navegador).** No hay UI que conducir; un E2E de browser no aplica. El equivalente para una API es el **smoke + Newman contra el stack real**, que sí existe y corre en CI.
- **`bmad-qa-generate-e2e-tests` — aplicabilidad.** Esta skill de BMAD genera tests E2E automatizados para features existentes (típicamente UI con Playwright/Cypress). En este proyecto headless **no se invocó**: su nicho (E2E de navegador) no aplica, y la cobertura de extremo a extremo ya la dan functional + integration + smoke/Newman. Queda como herramienta disponible si el proyecto incorporara un frontend; para la capa de API, la ruta equivalente es ampliar la colección Postman/Newman (como se hizo en la Story T.2).

---

## 5. Cómo correr las pruebas

```bash
# Suite completa (unit + integración; requiere Docker para Testcontainers), excepto el money test:
dotnet test HotelBookingHub.slnx -c Release --filter "Category!=G1"
# Money test de concurrencia, aislado (secuencial):
dotnet test HotelBookingHub.slnx -c Release --filter "Category=G1"

# Smoke + Newman contra el compose local (data-plane completo):
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build
JWT="$(grep '^JWT_SIGNING_KEY=' deploy/.env | cut -d= -f2-)"
GATEWAY=http://localhost:8080 JWT_SIGNING_KEY="$JWT" bash deploy/scripts/smoke.sh
newman run postman/hotel-booking-hub.postman_collection.json \
  --env-var "baseUrl=http://localhost:8080" --env-var "jwtSigningKey=$JWT"
```

En CI todo esto es un gate: `build + format + test` (con G1 aislado), `terraform fmt+validate`, `gitleaks`, y —en la integración hacia `main`— el `smoke-compose` (`docker compose up` + `/health` + Newman). Ver [`.github/workflows/ci.yml`](../.github/workflows/ci.yml).
