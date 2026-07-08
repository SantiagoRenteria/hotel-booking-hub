# Entrega, pruebas y plan por fases

Compañero de [SPEC.md](SPEC.md). Detalla los entregables, la estrategia TDD, el roadmap, la trazabilidad y el uso de IA.

## Requisitos de entrega

| Requisito | Cómo se cumple |
|-----------|----------------|
| Código fuente en repositorio **público** (GitHub) | Repo público sin dependencias privadas (ADR-009) |
| **README.md** con ejecución local, decisiones de arquitectura, ADRs y diagramas C4 | README derivado del contrato + `docs/` |
| Documentar prácticas de seguridad aplicadas y por qué | Ver [security-and-quality.md](security-and-quality.md) |
| Documentar el uso de herramientas de IA | Ver §"Uso de IA" abajo |
| **Colección Postman** o equivalente para flujos principales | Colección versionada + ejecución en CI con Newman |
| **docker-compose** funcional | `docker-compose.yml` reproducible (ADR-007) |

## Estrategia de pruebas (TDD)

- **TDD obligatorio:** tests antes del código de producción, ciclo Red → Green → Refactor con commits que evidencien el ciclo.
- **Flujo crítico elegido para TDD:** cálculo de precio de la reserva + creación de reserva con verificación de no-overbooking (el corazón del dominio). Se suma el **cálculo de la penalidad de cancelación** (regla de los 30 días / 0% vs 100%, con la fecha de solicitud como referencia) como segundo objetivo TDD de dominio puro.
- **Unit tests:** xUnit + EF Core InMemory (dominio, handlers, validators).
- **Integration tests:** xUnit + Testcontainers.MsSql (SQL Server real; imprescindible para probar la unicidad de slots anti-overbooking y el outbox).
- **API tests:** colección Postman ejecutada con Newman en CI.
- **Cobertura ≥ 80%** en código nuevo. Mínimos por tipo: validators (happy + cada regla), endpoints (200/400/401/403/404/500), handlers (happy + cada rama + excepciones).

## Plan por fases y entregables

| Fase | Objetivo | Entregables | Cubre |
|------|----------|-------------|-------|
| **0 · Discovery (BMAD)** | Documentación y esqueleto | Este contrato → PRD + `architecture.md` + `docs/adr/`; esqueleto .NET 10 + Aspire | IA-dev, DDD, entrega |
| **1 · Core blindado** | Funcionalidad base impecable | Hotels CRUD + búsqueda/reserva + anti-overbooking (slots) + **núcleo de cancelación (CAP-10/CAP-11: solicitud, política/penalidad, estado, liberación de slots)** + TDD del flujo crítico + Postman | HU1, HU2, cancelación, TDD, Clean Code |
| **2 · Diferenciadores** | Nivel senior/lead | Eventos Dapr + Notifications (reserva **y cancelación**) + Outbox/idempotencia (Redis) + seguridad (8 prácticas + OWASP) + observabilidad OTel | microservicios, RabbitMQ, seguridad, telemetría |
| **3 · Nube (con compuerta)** | Despliegue real | Terraform → ACA + Azure SQL + Cache for Redis + Service Bus + Key Vault + App Insights | infra avanzada, escalabilidad, cloud-native |
| **Transversal** | Entrega | README con C4 + ADRs, docker-compose, colección Postman/Newman, doc de uso de IA | requisitos de entrega |

**Regla de oro:** no se pasa de fase sin cerrar la anterior. Azure es lo primero que se recorta si el tiempo aprieta (queda como IaC documentada).

## Trazabilidad requisito → capacidad → solución

| Requisito / Criterio | CAP | Dónde se resuelve |
|----------------------|-----|-------------------|
| HU1-1..HU1-4 (CRUD + soft delete + enable/disable) | CAP-1, CAP-2 | `Hoteles.Api` — aggregate `Hotel`/`Habitacion`, `/api/v1/hoteles`, `/api/v1/habitaciones` |
| HU1-5 (listar reservas del agente) | CAP-3 | `Reservas.Api` — `ObtenerReservasAgenteQuery` (proyección) |
| HU2-1 (búsqueda por ciudad/fechas/huéspedes) | CAP-4 | `Reservas.Api` — `BuscarHabitacionesDisponiblesQuery` sobre `ProyeccionHabitacion` + slots + caché Redis |
| HU2-2 (iniciar reserva) | CAP-5 | `CrearReservaCommand` |
| HU2-3, HU2-4 (datos de huésped + emergencia) | CAP-5 | Aggregate `Reserva` (VO `Huesped`, `ContactoEmergencia`) |
| HU2-5 (notificación por correo) | CAP-7 | Evento `ReservaConfirmada` → `Notificaciones.Worker` → SMTP |
| Solicitud de cancelación + política sugerida | CAP-10 | `SolicitarCancelacionCommand` — VO `MotivoCancelacion`/`Iniciador`/`PenalidadSugerida`, guard de estado |
| Resolución (aprobar/condonar/rechazar) + auditoría | CAP-11 | `ResolverCancelacionCommand` — `PenalidadDecidida`, libera `NochesHabitacion` **solo al aprobar**, eventos `ReservaCancelada` / `SolicitudCancelacionRechazada` |
| Cero overbooking bajo concurrencia | CAP-6 | Slots `NochesHabitacion` + `UNIQUE` + `SERIALIZABLE` |
| Auth + RBAC | CAP-8 | JWT/OIDC + policies en Gateway y servicios |
| Observabilidad / tracing | CAP-9 | OTel + Aspire dashboard / App Insights |

## Uso de IA en el desarrollo (BMAD)

- **Herramientas:** Claude Code (agente principal) con el método BMAD (Analyst → PM → Architect → SM → Dev → QA).
- **Para qué:** análisis del enunciado y la vacante, generación del documento base, PRD y ADRs, fragmentación en historias, generación asistida de módulos críticos (repositorio, eventos/outbox, tests).
- **Cómo se verifica calidad/seguridad:** todo código pasa las reglas de [security-and-quality.md](security-and-quality.md), TDD, SAST/gitleaks en CI y revisión humana; los prompts de módulos críticos se guardan y se documenta la iteración.
- **Casos a documentar con prompt + iteración:** (1) repositorio de reservas con outbox + slots, (2) handler idempotente de eventos (Redis), (3) tests del flujo crítico de precio/reserva.
