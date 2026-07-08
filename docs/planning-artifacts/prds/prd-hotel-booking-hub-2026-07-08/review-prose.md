# Revisión de prosa — PRD hotel-booking-hub

**Método:** bmad-editorial-review-prose (copy-editing clínico). Solo claridad y corrección; no se altera el sentido ni las decisiones. Idioma: español.

**Veredicto:** El PRD está bien escrito y es comprensible; se detectan pocos problemas reales de prosa, la mayoría menores. El más relevante es un término inexistente en un encabezado de tabla y una mezcla español/inglés.

## Hallazgos

| Texto original | Texto propuesto | Cambios |
|----------------|-----------------|---------|
| `\| # \| Objetivo \| Métrica de éxito \| Contrarréstica \|` (§2, encabezado de tabla, línea 26) | `\| # \| Objetivo \| Métrica de éxito \| Contramétrica \|` | "Contrarréstica" no es una palabra en español y no aparece en el resto del documento. Consultar: por el contenido de la columna (guardarraíles que contrarrestan la métrica) parece pretenderse "Contramétrica" o "Contrapartida". Verificar cuál refleja la intención. |
| RabbitMQ local / Service Bus nube **por component** (NFR-6, línea 108) | RabbitMQ local / Service Bus nube **por componente** | Mezcla español/inglés: "component" debe ser "componente" (o reformular como "por configuración de componente"). |
| p95/p99 de búsqueda **se mantiene estable** bajo carga concurrente (G7, línea 34; y NFR-1, línea 103: "su p95/p99 **se mantiene estable**") | p95/p99 de búsqueda **se mantienen estables** | Concordancia: "p95/p99" designa dos métricas (sujeto plural); el verbo y el adjetivo deben ir en plural. Aparece en 2 ubicaciones (líneas 34 y 103). |
| La cancelación es Fase 1, pero se separa su **núcleo de dominio** (solicitud, política/penalidad, transición de estado y liberación de slots — FR-14…17), que va en **Fase 1**, de sus **notificaciones por correo** (FR-20, FR-21), que dependen de la infraestructura de eventos... (§6, línea 123) | Aunque la cancelación pertenece a la Fase 1, su **núcleo de dominio** (solicitud, política/penalidad, transición de estado y liberación de slots — FR-14…17) se separa de sus **notificaciones por correo** (FR-20, FR-21), que dependen de la infraestructura de eventos... | Claridad y redundancia: la construcción "se separa [X]... de [Y]" queda partida por un paréntesis largo, lo que dificulta seguir el par correlativo. Además "que va en Fase 1" repite lo ya dicho ("La cancelación es Fase 1"). Se acerca el "de [Y]" a su antecedente y se elimina la repetición, sin cambiar el sentido. |
| ante un fallo, **el span del servicio/operación exacto** es visible (FR-25, línea 96) | ante un fallo, **el span exacto del servicio/operación** es visible | Ambigüedad de referencia: "exacto" al final puede leerse como modificando "operación" en vez de "span". Anteponerlo a lo que califica ("el span exacto") elimina la duda. |
| Cero **overbooking** bajo concurrencia (G1, línea 27); F4 — Garantía **anti-overbooking** (línea 78); vs. "sobreventa" (Visión, línea 14) | Unificar: usar "sobreventa" en prosa corrida (ya introducido en §1) y mantener "anti-overbooking" solo como nombre técnico del invariante | Consistencia terminológica: el mismo concepto aparece como "sobreventa" y como "overbooking". Sugerencia (consultar): usar el término español en objetivos/prosa y reservar el anglicismo para el nombre fijo del patrón. |
| trace-id propagado **Gateway→servicio→sidecar→worker** (G4, línea 31) vs. **Gateway→servicio→sidecar Dapr→worker** (FR-25, línea 96) | Unificar ambas cadenas (p. ej. "Gateway→servicio→sidecar Dapr→worker" en las dos) | Consistencia: la misma cadena de propagación se escribe con y sin "Dapr". Usar la misma forma en las dos ubicaciones. |
| La escala objetivo es **≈10.000** reservas/día (**~0,12** writes/s promedio) (línea 131) | Usar un solo símbolo de aproximación (≈ o ~) en ambos valores | Consistencia menor: se mezclan "≈" y "~" para expresar aproximación en la misma frase. |

## Notas

- **Terminología en inglés intencional** (`core`, `back end`, `single-tenant`, `soft delete`, `slots`, `at-least-once`, `effectively-once`, `writes/s`, `retries/DLQ`): coherente con el registro técnico del documento; se preserva como voz del autor, no se marca como error.
- **Acentuación:** revisada; la ortografía con tildes es correcta en general (no se hallaron tildes faltantes).
- **Formato numérico:** separador de miles con punto y decimales con coma (10.000, 0,12) es correcto para español y se usa de forma consistente.
- **Contenido intacto:** no se propone ningún cambio que altere decisiones, alcance, políticas (p. ej. penalidad 0 %/100 %) ni requisitos. Todas las sugerencias son de forma.
