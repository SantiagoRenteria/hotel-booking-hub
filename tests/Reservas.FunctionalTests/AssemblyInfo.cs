// Story 7.2: MetricasDuracionTests usa un MeterListener PROCESS-WIDE que captura http.server.request.duration de
// TODO el proceso. Si otras clases de test HTTP corrieran en paralelo, contaminarían/desincronizarían sus
// mediciones (misma lección que el listener de tracing en 7.1). Se serializan las clases de este ensamblado
// (cada ensamblado de test ya corre en su propio proceso, así que el aislamiento es completo). Coste: ~14 tests
// rápidos en serie.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
