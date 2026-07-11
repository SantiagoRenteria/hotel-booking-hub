// CorrelacionTrazaTests (Story 7.1) usa un ActivityListener PROCESS-WIDE sobre el source "HotelBookingHub".
// DespachadorNotificaciones emite un span de consumidor desde ese mismo source, así que otras clases de test
// (p. ej. DespachadorNotificacionesTests) que corran EN PARALELO inyectan spans en el listener y pueden
// desincronizar sus aserciones (flaky de baja frecuencia). Se serializan las clases de este ensamblado (cada
// ensamblado de test corre en su propio proceso → aislamiento completo). Mismo patrón que Reservas.FunctionalTests.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
