# ADR-006 — JWT propio OIDC + RBAC (también en nube)

- **Decisión:** emisor JWT propio (OIDC simple) + RBAC server-side, **en local y en nube**; Entra ID descartado para mantener reproducibilidad y control end-to-end.
- **Consecuencias:** (+) reproducible y fácil de evaluar; sin dependencia de un IdP gestionado. (−) no es un IdP completo (aceptable al alcance); si se requiriera federación empresarial, habría que introducir Entra ID más adelante.
