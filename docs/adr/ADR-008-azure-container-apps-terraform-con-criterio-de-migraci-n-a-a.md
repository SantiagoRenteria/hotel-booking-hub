# ADR-008 — Azure Container Apps + Terraform (con criterio de migración a AKS)

- **Contexto:** dejar la app lista para nube y demostrar IaC. Se evaluó ACA vs AKS.
- **Decisión:** desplegar en ACA (Dapr y KEDA gestionados) + Azure SQL + Azure Cache for Redis, **exclusivamente por Terraform** (sin provisión manual ni click-ops); Fase 3 con compuerta. El entregable de nube es la IaC ejecutable. ACA corre sobre AKS por debajo.
- **Cuándo migraría a AKS (documentado, no ejecutado):** control fino (ingress controllers, network policies, service mesh), workloads no-serverless, multi-cloud. La migración es viable porque Dapr y los contenedores son los mismos.
- **Consecuencias:** (+) despliegue real y escalable sin operar K8s; demuestra criterio ACA↔AKS. (−) menos control fino que AKS (se documenta el camino de salida).
