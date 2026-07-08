# Catálogo de patrones de diseño

Compañero de [SPEC.md](SPEC.md). Se aplican **con intención** (no *pattern soup*): cada patrón resuelve un problema real del dominio.

## Creacionales
- **Factory Method / Static Factory:** `Hotel.Crear(...)`, `Habitacion.Crear(...)`, `Reserva.Crear(...)` — construyen aggregates garantizando invariantes, con setters privados. Punto único de creación válida.
- **Builder:** *test data builders* (Object Mother) para armar aggregates complejos en los tests de forma legible.

## Estructurales
- **Adapter (puertos y adaptadores):** EF Core, Dapr, Redis y SMTP son adaptadores de los puertos del dominio (`IReservaRepository`, `IPublicadorEventos`, `IServicioCorreo`, `IAlmacenCache`).
- **Decorator:** (1) pipeline de behaviors del mediator (`Validation → Logging → Transaction → Metrics`); (2) repositorio cacheado — `ProyeccionHabitacionRepositoryCache` decora al repositorio real añadiendo Redis sin que el handler se entere.
- **Facade:** los *Application services* / handlers actúan como fachada de casos de uso sobre el dominio.

## De comportamiento
- **Strategy** ⭐ (dos usos):
  - *Infraestructura (híbrido local/nube):* `IServicioCorreo` con estrategias SMTP (local, MailHog) vs Azure Communication Services / SendGrid (nube), seleccionadas por entorno.
  - *Dominio:* `IEstrategiaPrecio` — cálculo de precio intercambiable (base+impuestos, temporada alta/baja, descuento por estancia larga), extensible sin modificar el flujo de reserva (Open/Closed). Es el *flujo crítico de TDD*.
- **Factory + Strategy combinados:** una `ServicioCorreoFactory` (o el contenedor DI) resuelve qué estrategia instanciar según el entorno → híbrido local/nube.
- **Mediator:** mediator propio para CQRS.
- **Observer:** *domain events* dentro del dominio — `ReservaConfirmada`, `PrecioHabitacionCambiado`.
- **Specification** ⭐: reglas de negocio/consulta encapsuladas y componibles — `EspecificacionHabitacionDisponible`, `EspecificacionReservaSolapada` — reutilizables entre queries y validaciones.
- **Chain of Responsibility:** middleware del Gateway (auth → rate limit → correlation id → error handling).

## Arquitectónicos / de aplicación
**Repository + Unit of Work**, **CQRS**, **Result Pattern**, **Transactional Outbox + Inbox (idempotencia)**, **Options pattern** (`IOptions<T>`), **Dependency Injection**.

## CQRS y mediator propio
- **Commands** (escritura): pasan por el dominio y sus invariantes (`CrearReservaCommand`, `CrearHotelCommand`).
- **Queries** (lectura): optimizadas, `AsNoTracking()`, con caché Redis (`BuscarHabitacionesDisponiblesQuery`, `ObtenerReservasAgenteQuery`).
- **Mediator propio** (~30 líneas): `ISender` + `IRequestHandler<TReq,TRes>` + pipeline de behaviors (Decorator). Sin MediatR (licencia comercial) ni Wolverine (se solaparía con Dapr).

## Descartados con criterio (anti sobre-ingeniería)
- **Singleton** → lo cubre el contenedor DI con *lifetimes*.
- **State (máquina completa)** para el ciclo de la reserva → basta enum + guards en el aggregate.
- **Abstract Factory de adaptadores por entorno** → Dapr ya abstrae el proveedor.
- **Visitor / Interpreter / Memento / Flyweight** → sin caso real en este dominio.

> Principio: se prefiere **1 patrón bien aplicado y justificado** a 5 forzados.
