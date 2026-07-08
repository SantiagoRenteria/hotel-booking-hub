# PRD Quality Review — hotel-booking-hub (2026-07-08)

## Veredicto general

Este es un PRD fuerte y, para un entregable de prueba técnica, notablemente disciplinado: tiene una tesis explícita ("core impecable + pocos diferenciadores con profundidad + documentación que justifica cada decisión"), personas que efectivamente mueven los FRs, métricas ancladas a lo demostrable en test/CI (no vanity metrics) y una sección de no-objetivos que hace trabajo real. La delegación deliberada del detalle técnico a los companions del SPEC canónico es una decisión de diseño coherente, no un vacío, y las referencias cruzadas verificadas resuelven (companions, ADRs y CAPs existen). Lo que está en riesgo es acotado: la **transición de confirmación de la reserva** (el corazón del camino de escritura) no está capturada como FR explícito y queda ambigua entre un flujo de un paso y uno de dos ("inicia" vs. "Confirma"), y el PRD no incorpora glosario propio (se apoya en el del SPEC). Nada crítico ni roto.

**Compuerta:** PASA CON RESERVAS — apto como entregable de prueba técnica; conviene cerrar el FR de confirmación del ciclo de vida de reserva antes de generar historias.

## Decision-readiness — strong

Las decisiones se enuncian como decisiones, no como "consideraciones". Cada objetivo (§2) trae su **contrarréstica** explícita, que es exactamente lo que evita que el PRD "suavice todo a neutral" (G3 "sin sobre-resiliencia", G5 "0 diferenciadores a medias", G7 "sin optimización prematura"). Los trade-offs nombran lo que se cede: SQL Server en instancia única frente a alternativas distribuidas (justificado con ~0,12 writes/s), MongoDB diferido como evolución CQRS, Azure como primer recorte. Las preguntas abiertas (§7) están genuinamente resueltas: PA-1 no es una pregunta retórica con respuesta en la frase siguiente, sino una tensión de secuenciación cerrada con motivo arquitectónico ("Fase 1 = core puro; toda la mensajería asíncrona se concentra en Fase 2"). Un evaluador que empujara sobre "¿por qué congelar la penalidad en la fecha de solicitud?" encuentra su objeción anticipada y respondida (§7 ASSUMPTION). No hay findings.

## Substance over theater — strong

Sin teatro. Dos actores, dos protagonistas nombrados (Carolina, Andrés), y cada recorrido aterriza en FRs concretos — no hay personas de relleno ni el techo de "más de cuatro personas". Las NFRs traen umbrales específicos del producto (≈10.000 reservas/día, p95/p99 estables bajo carga, cobertura ≥80% en código nuevo, umbral de penalización de 30 días), no boilerplate de "el sistema debe ser escalable/seguro". La visión (§1) es específica de este producto ("garantiza por diseño que nunca haya sobreventa") y no intercambiable con cualquier PRD de la categoría. Las métricas de G1 (N concurrente aleatorio 30–100 con semilla registrada para reproducibilidad) son la antítesis del vanity metric. No hay findings.

## Strategic coherence — strong

El PRD tiene tesis y la ejecuta. El "principio rector" (§1) no es decorativo: ancla la priorización por fases (§6), donde el core blindado (Fase 1) precede a los diferenciadores (Fase 2) y a la nube (Fase 3 con compuerta), y la "regla de oro" ("no se pasa de fase sin cerrar la anterior") hace que la secuencia siga de la tesis y no de "lo fácil primero". Las métricas validan la tesis (G5 mide precisamente "lo que se entrega, se entrega completo"). El tipo de alcance MVP es coherente: problem-solving con núcleo blindado. No lee como backlog con encabezados.

### Findings
- **low** Solape métrica/NFR entre G7 y NFR-1 (§2, §5) — G7 ("lectura y escritura coexisten") es en esencia una NFR de rendimiento reformulada como objetivo; duplica NFR-1 y la garantía de G1. No daña, pero infla la lista de objetivos con un enunciado transversal. *Fix:* mantener G7 como objetivo solo si se lo trata como criterio de aceptación medible propio (p95/p99 de búsqueda bajo carga de escritura con umbral), o degradarlo a NFR-1.

## Done-ness clarity — adequate

La mayoría de los FRs traen al menos una consecuencia verificable: FR-8 acota el resultado de búsqueda con precisión (activas, capacidad ≥ huéspedes, rango `[entrada, salida)`, excluye reservadas); FR-15 fija la política de penalización con números (≥30 días → 0%; <30 días → 100%); FR-18 ancla la garantía anti-overbooking en el motor de datos con 409 al perdedor; FR-17 exige guards de transición y actor. La dimensión de "adjetivos en vez de cotas" está mayormente controlada, y donde hay un objetivo suave (G2 "tiempo razonable") está explícitamente marcado como tal. Es la dimensión con las reservas de esta revisión.

### Findings
- **high** La transición de confirmación de reserva no es un FR explícito (§3, §4 F2/F3) — el journey de Andrés distingue "inicia la reserva registrando datos" y luego "Confirma: ... la reserva queda confirmada", pero ningún FR modela ese paso: FR-9 dice "inicia una reserva", FR-12 "calcula el precio total y lo expone **al confirmar**", y el estado `Confirmada` recién aparece nombrado en FR-14 (cancelación). Queda ambiguo si la creación de reserva es una operación atómica de un paso o un flujo draft→confirm de dos pasos — decisión que determina el contrato de API y es la puerta de entrada al ciclo de vida que gobierna toda F3. *Fix:* añadir un FR que defina explícitamente la operación de confirmación (uno o dos pasos), el estado resultante `Confirmada` y su atomicidad respecto de la reserva de slots (FR-18).
- **medium** FR-10/FR-11 listan campos pero no reglas de obligatoriedad/validación (§4 F2) — "registra los datos completos de cada huésped" enumera campos (documento, fecha de nacimiento, email, teléfono…) sin decir cuáles son obligatorios, ni formato/validación (p. ej. email válido, documento no vacío). La creación de historias necesitaría esto o lo inventaría. *Fix:* marcar campos obligatorios vs. opcionales y validaciones mínimas, o referenciar explícitamente el companion donde vive el contrato de datos.
- **low** G2 usa cota-adjetivo ("arranque en frío en tiempo razonable") (§2) — autoetiquetada "objetivo suave", por lo que no es un falso positivo, pero no es verificable como está. *Fix:* opcional para prueba técnica; si se desea, fijar un techo (p. ej. "smoke `/health` verde en ≤ N s").

## Scope honesty — strong

Ejemplar. La sección de no-objetivos (§7) hace trabajo real y desactiva suposiciones silenciosas: multi-tenant, modificación de reserva (resuelta como cancelar+recrear), rechazo de cancelación por el agente, read model en MongoDB, pagos/PCI, IdP externo, front-end, click-ops. Los `[ASSUMPTION]` están tagueados inline y son inferencias reales (congelación de penalidad en fecha de solicitud; el agente solo aprueba), y ambos reaparecen como no-objetivos coherentes. El de-scoping es honesto y explícito (Azure como primer recorte; MongoDB documentado pero no implementado). Densidad de ítems abiertos: adecuada a los stakes — cero preguntas abiertas pendientes, dos assumptions acotadas. No hay findings.

## Downstream usability — adequate

PRD chain-top (alimenta arquitectura e historias), así que esta dimensión pesa. Los IDs de FR son contiguos y únicos (FR-1…FR-26), y la correspondencia FR→CAP (§8) es explícita y **verificada** contra los companions (ADR-002/007/008/011/013/014 y CAP-6/7/10/11 resuelven en `decisions-adr.md` y `delivery-and-testing.md`). Cada sección se sostiene extraída por separado. La debilidad es la ausencia de glosario propio.

### Findings
- **low** Sin glosario en el PRD; se apoya en el del SPEC (`glossary.md`) (§8) — términos de dominio como "slots" de inventario, "ofertabilidad" y los nombres de estado (`Confirmada`, `CancelacionSolicitada`, `Cancelada`) se usan de forma consistente, pero un consumidor downstream del PRD aislado no tiene definición local. Mitigado porque el SPEC canónico incluye glosario y el PRD lo referencia. *Fix:* añadir una línea en §8 que apunte explícitamente al `glossary.md` del SPEC como fuente de términos, o un mini-glosario de los 5–6 nouns de estado/inventario.

## Shape fit — strong

La forma calza con el producto. Aunque es un back end de un único tenant, es multi-actor (Agente + Viajero) con flujos reales, así que los mini-journeys con protagonista nombrado son load-bearing y no sobre-formalización — el PRD acierta al incluirlos sin inflarlos (§3 es breve y remite el detalle a §4). Correctamente **omite** una dimensión/sección de UX (entrega exclusivamente back end, declarado como no-objetivo), evitando la trampa de forzar una plantilla de producto de consumo. La métrica es operacional/verificable (test/CI/demo), apropiada para un spec de capacidad técnica en lugar de user-facing. No hay findings.

## Mechanical notes

- **IDs:** FR-1…FR-26 contiguos y únicos; sin huecos ni duplicados. G1…G7 y NFR-1…NFR-8 consistentes.
- **Referencias cruzadas:** todos los companions citados existen en `docs/specs/spec-hotel-booking-hub/` (stack-and-conventions, concurrency-and-messaging, security-and-quality, architecture-diagrams, decisions-adr, patterns, delivery-and-testing, glossary). ADRs (002/007/008/011/013/014) y CAPs (6/7/10/11) resuelven en los companions. Rutas relativas correctas.
- **Assumptions roundtrip:** los 2 `[ASSUMPTION]` inline (§7) reaparecen coherentemente como no-objetivos; sin huérfanos.
- **Protagonistas de journeys:** ambos UJ tienen protagonista nombrado (Carolina, Andrés) con contexto inline. Sin journeys flotantes.
- **Deriva de glosario:** nombres de estado y términos de inventario usados de forma uniforme en §3/§4/§5/§7; sin drift de caso/plural/sinónimos detectado.
- **[NUEVO] tags:** FR-14…17, FR-20, FR-21 marcados como ampliación del contrato (cancelación) de forma consistente con la nota de trazabilidad de §8 (CAP-10/11, ADR-014).
