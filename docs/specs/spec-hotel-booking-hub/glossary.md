# Lenguaje ubicuo, Bounded Contexts y modelo de dominio

Compañero de [SPEC.md](SPEC.md). Sostiene la constraint de ≥2 Bounded Contexts y las capacidades CAP-1..CAP-7.

## Bounded Contexts

Dos contextos con lenguaje y responsabilidades separadas. **Sin FK físicas entre servicios**; el cruce se resuelve por **proyecciones** alimentadas por eventos.

- **BC Hoteles** (catálogo) habla el lenguaje del **agente**: gestión, comisiones, estados de publicación.
- **BC Reservas** habla el lenguaje del **viajero**: disponibilidad, estancia, huésped.
- El invariante de **no-overbooking pertenece solo a Reservas**. El precio base y los impuestos nacen en Hoteles y llegan a Reservas como **dato proyectado**.
- **BC Notificaciones** (worker): sin BD relacional de dominio; consume eventos y envía correo.

## Términos ubicuos (código en español, sin tildes)

| Concepto (negocio) | Término ubicuo | BC |
|--------------------|----------------|----|
| Hotel | `Hotel` | Hoteles |
| Habitación | `Habitacion` | Hoteles |
| Tipo de habitación | `TipoHabitacion` | Hoteles |
| Reserva | `Reserva` | Reservas |
| Huésped | `Huesped` | Reservas |
| Contacto de emergencia | `ContactoEmergencia` | Reservas |
| Estancia (rango de fechas) | `Estancia` / `RangoFechas` | Reservas |
| Disponibilidad | `Disponibilidad` | Reservas |
| Noche de habitación (slot) | `NocheHabitacion` | Reservas |
| Estado de la reserva | `EstadoReserva` (enum) | Reservas |
| Solicitud de cancelación | `SolicitudCancelacion` | Reservas |
| Política de cancelación (default) | `PoliticaCancelacion` | Reservas |
| Penalidad sugerida / decidida | `PenalidadSugerida` / `PenalidadDecidida` | Reservas |
| Motivo de cancelación | `MotivoCancelacion` | Reservas |
| Iniciador de la solicitud | `Iniciador` | Reservas |
| Agente | `Agente` (rol) | ambos |
| Viajero | `Viajero` (rol) | Reservas |

## Modelo de dominio

### BC Hoteles (`Hoteles.Api`)
- **Aggregate root:** `Hotel` (contiene sus `Habitacion` como entidades hijas).
- **Value Objects:** `Direccion` (ciudad, línea…), `Dinero` (costoBase), `Impuesto`, `TipoHabitacion`, `UbicacionHabitacion`.
- **Domain events:** `HotelCreado`, `HotelDeshabilitado`, `HabitacionAgregada`, `PrecioHabitacionCambiado`, `HabitacionDeshabilitada`.
- **Invariantes:** una habitación deshabilitada o de un hotel deshabilitado no puede ofertarse.

### BC Reservas (`Reservas.Api`)
- **Aggregate root:** `Reserva` (contiene `Huesped`(es), `ContactoEmergencia`, `Estancia`, `EstadoReserva` y, cuando aplica, una `SolicitudCancelacion`).
- **Ciclo de vida (`EstadoReserva`):** `Confirmada → CancelacionSolicitada → {Cancelada (aprobada) | Confirmada (rechazada)}`, con **guards** en el aggregate (transiciones inválidas y actores no autorizados se rechazan; no se puede solicitar cancelación de una estancia ya iniciada/pasada ni de una reserva con solicitud en curso).
- **Value Objects:** `Estancia` (entrada, salida — `DateOnly`), `NumeroHuespedes`, `ContactoEmergencia`, `Documento` (tipo + numero), `Dinero`, `PoliticaCancelacion` (umbral de días + porcentaje del default), `PenalidadSugerida` (porcentaje + fecha de cálculo + regla aplicada), `PenalidadDecidida` (porcentaje + quién decidió + fecha + justificación), `MotivoCancelacion` (categoría + texto libre + origen `DeclaradoPorViajero`/`RegistradoPorAgente` + `CapturadoPor`), `Iniciador` (`Viajero`/`Agente` + id).
- **`SolicitudCancelacion` (entidad de la reserva):** agrupa `MotivoCancelacion`, `Iniciador`, `PenalidadSugerida`, `PenalidadDecidida` (null hasta resolver), estado (pendiente/aprobada/rechazada), motivo de rechazo, timestamps y "días en espera" (derivado). El agente puede **solicitar y resolver en una sola operación** (atajo telefónico), registrando ambos eventos para auditoría.
- **Política de cancelación (default sugerido, no impuesto):** referencia = fecha de la solicitud; ≥30 días al check-in ⇒ 0%, <30 días ⇒ 100% del valor. El motor calcula la `PenalidadSugerida` (congelada al solicitar); el agente fija la `PenalidadDecidida` con discreción (aplicar, condonar o rechazar). Sin cobro real: se registra como monto adeudado.
- **Slots de inventario:** `NocheHabitacion` (habitacionId, noche, reservaId) con `UNIQUE(habitacionId, noche)` — mecanismo anti-overbooking (ver [concurrency-and-messaging.md](concurrency-and-messaging.md)). Los slots siguen **ocupados** mientras la solicitud está pendiente (`CancelacionSolicitada`); se **liberan solo al aprobar** la cancelación; un rechazo no los toca.
- **Read model:** `ProyeccionHabitacion` (copia local read-only de habitaciones: id, hotelId, ciudad, tipo, costoBase, impuesto, capacidad, activa) actualizada por eventos de Hoteles vía Dapr. Consultas con índices + `AsNoTracking()`; Redis cachea resultados de búsqueda.
- **Domain events:** `ReservaConfirmada` (notificación de reserva), `SolicitudCancelacionRegistrada` (avisa al agente por resolver + acuse con estimación al viajero), `ReservaCancelada` (aprobación: libera slots y notifica; payload **enriquecido** con `HotelId`, tipo de habitación y fechas liberadas para el futuro waitlist), `SolicitudCancelacionRechazada` (la reserva vuelve a `Confirmada`; notifica al viajero con el motivo del rechazo).
- **Invariante central:** no pueden existir dos reservas activas para la misma `HabitacionId` con `Estancia` solapada.

### BC Notificaciones (`Notificaciones.Worker`)
- Usa **Redis** como inbox de idempotencia (message-id procesados con TTL).
- Consume `ReservaConfirmada` → envía correo a huésped y agente.
- Consume `SolicitudCancelacionRegistrada` → avisa al agente (por resolver) y envía al viajero un **acuse con la penalidad estimada** (marcada como estimación, no cobro final).
- Consume `ReservaCancelada` → notifica al viajero (y al agente) la cancelación efectiva con la penalidad final (o su condonación).
- Consume `SolicitudCancelacionRechazada` → notifica al viajero que su reserva **sigue Confirmada** y el motivo del rechazo.
