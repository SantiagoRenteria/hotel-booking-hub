# ADR-007 — Aspire para desarrollo + docker-compose mantenido a mano

- **Decisión:** Aspire como fuente de verdad de la topología (dev + OTel); el `docker-compose` se **mantiene a mano** (no se genera desde `Aspire.Hosting.Docker`) y se blinda con un smoke test en CI (`docker compose up` + verificación de `/health`) que detecta drift.
- **Consecuencias:** (+) control total del compose (incluidos sidecars Dapr) + entrega reproducible + observabilidad. (−) dos representaciones a mantener en sincronía (mitigado con el smoke test en CI).
