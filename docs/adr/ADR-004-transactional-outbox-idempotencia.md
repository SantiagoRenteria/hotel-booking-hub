# ADR-004 — Transactional Outbox + idempotencia

- **Decisión:** Outbox en la misma tx que la reserva; inbox por `message-id` en Redis; DLQ + retries.
- **Consecuencias:** (+) entrega *at-least-once* sin pérdida + procesamiento *effectively-once*. (−) tabla outbox y relay adicionales.
