# ADR-013 — Read model CQRS en MongoDB (diferido)

- **Decisión:** diseñar y documentar el read model en MongoDB (proyección desnormalizada por eventos, con seguridad SCRAM/RBAC/TLS/cifrado en reposo + CSFLE para PII), pero **no implementarlo** en esta entrega. Redis cubre el caché mientras tanto.
- **Consecuencias:** (+) demuestra dominio de CQRS y seguridad de datos sin añadir una BD más que asegurar/sincronizar/desplegar. (−) la separación write/read queda como evolución, no como código entregado.
