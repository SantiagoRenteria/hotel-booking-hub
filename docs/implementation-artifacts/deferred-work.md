# Trabajo diferido

Hallazgos reales pero no accionables ahora, registrados para no perderlos.

## Deferred from: code review of story-1.1 (2026-07-08)

- **`gitleaks-action@v2` y repos de organización** — la acción es gratuita en cuentas personales; en un repo propiedad de una GitHub Organization exige `GITLEAKS_LICENSE`. Hoy el repo es personal (funciona). Si se mueve a un org, o migrar a invocar el binario/imagen de gitleaks directamente (sin la action), o proveer la licencia. `[.github/workflows/ci.yml]`
- **`depends_on` sin `condition: service_healthy` + infra sin healthchecks** — hoy inocuo (los servicios del esqueleto no conectan a SQL/Redis/RabbitMQ al arrancar). En cuanto un servicio abra conexión/migración en el arranque (Story 1.5+), el orden de cold-start puede provocar crashes en el primer boot. Sembrar healthchecks de infra + `condition: service_healthy` entonces. `[deploy/docker-compose.yml]`
- **Smoke test con confianza parcial** — el smoke valida `/health` de los 4 servicios, que reportan `Healthy` aunque las BD/broker no arranquen (no los consumen aún). Aceptable como límite del gate en el esqueleto; reforzar (healthchecks de infra / prueba de conectividad) cuando haya lógica de negocio que dependa de ellos. `[.github/workflows/ci.yml, deploy/docker-compose.yml]`

## Deferred from: code review of story-1.4 (2026-07-08)

- **Precio: clase concreta `CalculadorPrecio` vs `IEstrategiaPrecio` (Strategy)** — `patterns.md` describe el precio como estrategia extensible (temporada alta/baja, descuento por estancia larga), pero `architecture.md` lo fija como domain service puro concreto para el alcance actual (solo base+impuesto). YAGNI ahora; si entran múltiples estrategias de precio, refactorizar hacia interfaz + inyección. `[Reservas.Domain/Servicios/CalculadorPrecio.cs]`
