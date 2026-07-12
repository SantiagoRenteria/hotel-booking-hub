# ADR-015 — Arranque sin `aspire-starter`: AppHost + ServiceDefaults a medida

- **Contexto:** la plantilla `aspire-starter` scaffoldea una app de muestra (Blazor Web + API + tests) que no aplica a un back end puro con estructura DDD a medida.
- **Decisión:** partir de una solución vacía + `aspire-apphost` + `aspire-servicedefaults` (Aspire 13, NuGet-only vía `Aspire.AppHost.Sdk`), y añadir los servicios de dominio a mano según la estructura del contrato; gobernanza de versiones con Central Package Management (`Directory.Packages.props`) y `Directory.Build.props` desde el primer commit. El Gateway se crea con `dotnet new web` (no `webapi`).
- **Consecuencias:** (+) cero código muerto que el evaluador deba descartar; orquestación + OpenTelemetry reproducibles; estructura folder-per-bounded-context legible. (−) el `Program.cs` de cada servicio se escribe a mano en vez de heredarlo del template.
