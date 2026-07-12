# ADR-012 — Redis para caché, idempotencia y state

- **Decisión:** Redis como caché de disponibilidad (Reservas), **caché de la lectura del catálogo** (Hoteles — listas paginadas, con **invalidación por generación** para evitar datos obsoletos, Story T.6), inbox de idempotencia (message-id con TTL) y Dapr state store.
- **Consecuencias:** (+) lecturas rápidas, idempotencia simple, alineado al stack. (−) una dependencia de infra más (gestionada por Aspire/compose/ACA). Ambas cachés son **"Redis-si-configurado"**: sin cadena de Redis, el servicio degrada a SQL directo (no rompe unit/integración sin Redis).
