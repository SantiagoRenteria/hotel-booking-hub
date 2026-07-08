---
title: "Revisión estructural — PRD hotel-booking-hub"
review-type: editorial-structure
reviewer: bmad-editorial-review-structure
date: 2026-07-08
target: docs/planning-artifacts/prds/prd-hotel-booking-hub-2026-07-08/prd.md
---

## Document Summary

- **Purpose:** PRD que define visión, objetivos, actores, features/FR, NFR y alcance por fases del back end `hotel-booking-hub`, sirviendo a la vez como entregable de una prueba técnica evaluada de alcance acotado.
- **Audience:** Doble — el evaluador de la prueba técnica (juzga razonamiento y decisiones) y el equipo/agente que implementa (deriva epics y stories del PRD + companions del SPEC).
- **Reader type:** humans
- **Structure model:** Strategic/Context (Pyramid) — conclusión/visión arriba, contexto agrupado debajo, alcance y trazabilidad al final.
- **Current length:** ~2.100 palabras en 8 secciones de nivel 2.

## Evaluación general

El documento está **bien construido para su propósito**: aplica el modelo Pyramid correctamente (Visión → Objetivos → Actores → Features → NFR → Alcance → Supuestos → Trazabilidad), usa tablas donde aportan (§2 objetivos, §6 fases) y prosa/listas donde corresponde (§4 FR). El uso deliberado de referencias a los companions del SPEC en §5 y §8 es **correcto y debe preservarse** — evita duplicar el detalle técnico y respeta "one source of truth".

Los problemas estructurales son **acotados y de segundo orden**: no hay fallos de orden de lectura ni burying de información crítica. Lo que sobra es **duplicación de contenido entre secciones** (objetivos↔NFR, journeys↔FR, y la resolución de PA-1 repetida en §6 y §7) y **una línea de trazabilidad redundante** en §8. Corregirlos reduce ~15-20% del texto sin perder comprensión y refuerza la impresión de rigor que la prueba evalúa.

No se detectan violaciones de alcance reales (las referencias al SPEC son intencionales) ni secciones que deban cortarse por completo.

## Recommendations

### 1. CONDENSE + MERGE — G7 (§2) vs NFR-1 (§5)
**Rationale:** La celda "Métrica de éxito" de G7 duplica casi textualmente el mecanismo de NFR-1 ("la lectura se sirve por proyección + caché Redis y la escritura bloquea solo los slots..."); G7 debe enunciar el objetivo medible y NFR-1 el mecanismo, sin repetir el mecanismo en ambos.
**Impact:** ~60 palabras
**Comprehension note:** Ninguna — dejar el "cómo" solo en NFR-1 (que ya referencia el SPEC) mejora la separación objetivo/mecanismo.

### 2. CUT — Línea "Correspondencia FR → CAP" (§8)
**Rationale:** El mapeo F→CAP ya aparece en el encabezado de cada feature de §4 (`F1 ... · CAP-1, CAP-2`, etc.); repetirlo comprimido en §8 es redundancia pura, no reinforcement.
**Impact:** ~40 palabras
**Comprehension note:** Ninguna — el lector encuentra la correspondencia en cada F-header, que es donde la necesita.

### 3. MERGE — Resolución de PA-1 duplicada entre §6 (Nota de secuenciación) y §7 (Preguntas abiertas)
**Rationale:** La "Nota de secuenciación (resuelta)" en §6 y el bloque "Preguntas abiertas → Ninguna pendiente. PA-1... se resolvió" en §7 explican lo mismo dos veces; la explicación completa debe vivir una sola vez (§6, donde está el plan por fases) y §7 solo remitir a ella.
**Impact:** ~50 palabras
**Comprehension note:** Ninguna — mantener la nota en §6 (contexto de fases) y reducir §7 a "Ninguna pendiente (PA-1 resuelta, ver §6)".

### 4. CONDENSE — Mini-journeys de §3 (Carolina / Andrés)
**Rationale:** Los recorridos narran secuencialmente casi todos los FR de §4 en prosa; aportan el "por qué" (valor real para humanos, preservar el formato), pero pueden acortarse ~25% eliminando el detalle de campos ("nombres, documento, fecha de nacimiento, género, email, teléfono") que ya es contrato en FR-10/FR-11.
**Impact:** ~90 palabras
**Comprehension note:** Los journeys aportan mental model y motivación — **preservar su existencia**; solo recortar el detalle de datos que duplica los FR, remitiendo a §4.

### 5. CUT — Duplicación "rechazo de cancelación fuera de alcance" (§7 Supuestos ↔ §7 No-objetivos)
**Rationale:** El hecho de que el agente solo aprueba (no rechaza) aparece como [ASSUMPTION] en Supuestos y de nuevo como No-objetivo dentro de la misma §7; basta enunciarlo una vez, como no-objetivo.
**Impact:** ~25 palabras
**Comprehension note:** Ninguna — es la misma decisión; su lugar natural es No-objetivos.

### 6. PRESERVE — §1 "Principio rector" y las referencias al SPEC (§5, §8)
**Rationale:** El "Principio rector" ancla explícitamente el criterio de evaluación a las decisiones de alcance (core impecable + pocos diferenciadores) — es el hilo argumental que justifica el PRD ante el evaluador; las referencias al SPEC evitan duplicar detalle técnico.
**Impact:** — (se conserva)
**Comprehension note:** Cortar cualquiera de los dos dañaría la coherencia estratégica y la trazabilidad del entregable.

## Summary

- **Total recommendations:** 6 (5 accionables + 1 preserve)
- **Estimated reduction:** ~265 palabras (~13% del original)
- **Meets length target:** No target specified — la reducción es proporcional a los stakes (prueba técnica acotada); no se justifica un recorte agresivo que sacrifique el razonamiento visible que la prueba valora.
- **Comprehension trade-offs:** Ninguno relevante. Las cinco recomendaciones eliminan duplicación entre secciones, no comprensión; la única con matiz (§3 journeys) preserva el formato narrativo y solo poda el detalle de datos ya cubierto por los FR.
