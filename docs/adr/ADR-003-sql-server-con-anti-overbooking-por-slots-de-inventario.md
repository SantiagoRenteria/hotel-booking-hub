# ADR-003 — SQL Server con anti-overbooking por slots de inventario

- **Contexto:** SQL Server es el motor de UltraGroup; el invariante exige garantías fuertes bajo concurrencia; SQL Server no tiene *exclusion constraints*.
- **Decisión:** tabla de slots `NochesHabitacion` con clave única `(HabitacionId, Noche)`; el conflicto se arbitra en el propio INSERT (el nivel de aislamiento se decide en ADR-016).
- **Consecuencias:** (+) cero overbooking garantizado por el motor, portable, soporta disponibilidad parcial. (−) N filas por reserva (irrelevante); menos escalado horizontal de escritura que NoSQL (no necesario a esta escala).
