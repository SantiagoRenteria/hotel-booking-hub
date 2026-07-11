# ADR-005 — CQRS con mediator propio

- **Contexto:** MediatR pasó a licencia comercial; se busca repo público limpio.
- **Decisión:** mediator propio minimalista con pipeline de behaviors; Wolverine descartado por solaparse con Dapr.
- **Consecuencias:** (+) cero dependencia de licencia, demuestra dominio del patrón. (−) mantener ~30 líneas propias.
