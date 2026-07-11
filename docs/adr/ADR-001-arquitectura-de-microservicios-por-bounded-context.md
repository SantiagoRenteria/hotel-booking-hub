# ADR-001 — Arquitectura de microservicios por Bounded Context

- **Contexto:** el enunciado premia separar ≥2 dominios con contratos y trade-offs.
- **Decisión:** 2 microservicios (Hoteles, Reservas) + Gateway + Worker, comunicados por eventos.
- **Consecuencias:** (+) despliegue/escala independiente, límites claros. (−) complejidad operativa y consistencia eventual (mitigada con proyecciones + outbox).
