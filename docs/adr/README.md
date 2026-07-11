# Registro de decisiones de arquitectura (ADR)

Cada decisión en su archivo (Contexto · Decisión · Consecuencias). Origen consolidado: [`decisions-adr.md`](../specs/spec-hotel-booking-hub/decisions-adr.md).

| ADR | Decisión |
|---|---|
| [ADR-001](ADR-001-arquitectura-de-microservicios-por-bounded-context.md) | Arquitectura de microservicios por Bounded Context |
| [ADR-002](ADR-002-dapr-como-runtime-de-pub-sub-y-secretos.md) | Dapr como runtime de pub/sub y secretos |
| [ADR-003](ADR-003-sql-server-con-anti-overbooking-por-slots-de-inventario.md) | SQL Server con anti-overbooking por slots de inventario |
| [ADR-004](ADR-004-transactional-outbox-idempotencia.md) | Transactional Outbox + idempotencia |
| [ADR-005](ADR-005-cqrs-con-mediator-propio.md) | CQRS con mediator propio |
| [ADR-006](ADR-006-jwt-propio-oidc-rbac-tambi-n-en-nube.md) | JWT propio OIDC + RBAC (también en nube) |
| [ADR-007](ADR-007-aspire-para-desarrollo-docker-compose-mantenido-a-mano.md) | Aspire para desarrollo + docker-compose mantenido a mano |
| [ADR-008](ADR-008-azure-container-apps-terraform-con-criterio-de-migraci-n-a-a.md) | Azure Container Apps + Terraform (con criterio de migración a AKS) |
| [ADR-009](ADR-009-sin-dependencias-privadas.md) | Sin dependencias privadas |
| [ADR-010](ADR-010-resiliencia-selectiva.md) | Resiliencia selectiva |
| [ADR-011](ADR-011-openapi-como-contrato-scalar-como-ui.md) | OpenAPI como contrato, Scalar como UI |
| [ADR-012](ADR-012-redis-para-cach-idempotencia-y-state.md) | Redis para caché, idempotencia y state |
| [ADR-013](ADR-013-read-model-cqrs-en-mongodb-diferido.md) | Read model CQRS en MongoDB (diferido) |
| [ADR-014](ADR-014-cancelaci-n-de-reservas-pol-tica-default-discreci-n-del-agen.md) | Cancelación de reservas: política default + discreción del agente |
| [ADR-015](ADR-015-arranque-sin-aspire-starter-apphost-servicedefaults-a-medida.md) | Arranque sin `aspire-starter`: AppHost + ServiceDefaults a medida |
| [ADR-016](ADR-016-arbitraje-del-invariante-por-ndice-nico-read-committed-en-ve.md) | Arbitraje del invariante por índice único (READ COMMITTED) en vez de SERIALIZABLE |
| [ADR-017](ADR-017-estrategia-de-claves-uuid-v7-como-identidad-clustering-key-s.md) | Estrategia de claves: UUID v7 como identidad + clustering key secuencial |
| [ADR-018](ADR-018-contrato-del-mediator-propio-y-atomicidad-del-outbox.md) | Contrato del mediator propio y atomicidad del outbox |
| [ADR-019](ADR-019-transporte-de-eventos-por-strategy-seg-n-entorno-rabbitmq-lo.md) | Transporte de eventos por Strategy según entorno (RabbitMQ local / Dapr nube) |
| [ADR-020](ADR-020-gesti-n-de-secretos-por-entorno-env-vars-local-dapr-secrets.md) | Gestión de secretos por entorno (env vars local / Dapr Secrets + Key Vault nube) |
| [ADR-021](ADR-021-cd-por-oidc-federated-despliegue-on-demand-con-aprobaci-n-ce.md) | CD por OIDC federated + despliegue on-demand con aprobación (cero secretos) |
| [ADR-022](ADR-022-state-remoto-de-terraform-por-bootstrap-az-backend-aad-dos-r.md) | State remoto de Terraform por bootstrap `az` + backend AAD + dos resource groups |
| [ADR-023](ADR-023-scale-to-zero-selectivo-worker-de-notificaciones-min-1-resto.md) | Scale-to-zero selectivo: worker de Notificaciones `min=1`, resto a cero |
