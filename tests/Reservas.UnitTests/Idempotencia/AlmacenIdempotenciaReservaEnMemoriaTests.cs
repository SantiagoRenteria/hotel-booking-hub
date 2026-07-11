using Reservas.Infrastructure.Idempotencia;

namespace Reservas.UnitTests.Idempotencia;

/// <summary>
/// Story 1.7 — contrato del almacén de idempotencia (fallback en memoria): reservar (SETNX), obtener, finalizar,
/// liberar. El adaptador Redis comparte el contrato; aquí se verifica la lógica determinista sin infraestructura.
/// </summary>
public sealed class AlmacenIdempotenciaReservaEnMemoriaTests
{
    private readonly AlmacenIdempotenciaReservaEnMemoria _almacen = new(new OpcionesIdempotenciaReserva());

    [Fact]
    public async Task Clave_ausente_devuelve_null()
    {
        Assert.Null(await _almacen.ObtenerAsync("nueva", CancellationToken.None));
    }

    [Fact]
    public async Task Reservar_gana_la_primera_vez_y_pierde_la_segunda()
    {
        Assert.True(await _almacen.ReservarAsync("K", "huella-1", CancellationToken.None));
        Assert.False(await _almacen.ReservarAsync("K", "huella-1", CancellationToken.None));
    }

    [Fact]
    public async Task Reservar_deja_la_clave_en_curso_hasta_finalizar()
    {
        await _almacen.ReservarAsync("K", "huella-1", CancellationToken.None);

        var enCurso = await _almacen.ObtenerAsync("K", CancellationToken.None);
        Assert.NotNull(enCurso);
        Assert.Equal("huella-1", enCurso!.HuellaCuerpo);
        Assert.Null(enCurso.RespuestaJson); // "en curso": sin respuesta todavía.

        await _almacen.FinalizarAsync("K", "huella-1", "{\"id\":\"x\"}", CancellationToken.None);
        var finalizada = await _almacen.ObtenerAsync("K", CancellationToken.None);
        Assert.Equal("{\"id\":\"x\"}", finalizada!.RespuestaJson);
    }

    [Fact]
    public async Task Liberar_permite_reservar_de_nuevo()
    {
        await _almacen.ReservarAsync("K", "huella-1", CancellationToken.None);

        await _almacen.LiberarAsync("K", CancellationToken.None);

        Assert.Null(await _almacen.ObtenerAsync("K", CancellationToken.None));
        Assert.True(await _almacen.ReservarAsync("K", "huella-2", CancellationToken.None));
    }
}
