using Reservas.Application.Abstracciones;
using Reservas.Infrastructure.Idempotencia;

namespace Reservas.UnitTests.Idempotencia;

/// <summary>
/// Story 1.7 — contrato del almacén de idempotencia (fallback en memoria): SETNX (guardar-si-ausente) + lectura.
/// El adaptador Redis comparte el contrato; aquí se verifica la lógica determinista sin infraestructura.
/// </summary>
public sealed class AlmacenIdempotenciaReservaEnMemoriaTests
{
    private readonly AlmacenIdempotenciaReservaEnMemoria _almacen = new();

    [Fact]
    public async Task Clave_ausente_devuelve_null()
    {
        Assert.Null(await _almacen.ObtenerAsync("nueva", CancellationToken.None));
    }

    [Fact]
    public async Task Guardar_si_ausente_persiste_la_primera_vez_y_se_puede_leer()
    {
        var registro = new RegistroIdempotencia("huella-1", "{\"id\":\"x\"}");

        var guardado = await _almacen.GuardarSiAusenteAsync("K", registro, CancellationToken.None);

        Assert.True(guardado);
        var leido = await _almacen.ObtenerAsync("K", CancellationToken.None);
        Assert.Equal(registro, leido);
    }

    [Fact]
    public async Task Guardar_si_ausente_no_sobrescribe_una_clave_existente()
    {
        var primero = new RegistroIdempotencia("huella-1", "{\"id\":\"1\"}");
        var segundo = new RegistroIdempotencia("huella-2", "{\"id\":\"2\"}");
        await _almacen.GuardarSiAusenteAsync("K", primero, CancellationToken.None);

        var segundoGuardado = await _almacen.GuardarSiAusenteAsync("K", segundo, CancellationToken.None);

        // SETNX: la segunda escritura NO gana; se conserva el primer registro.
        Assert.False(segundoGuardado);
        Assert.Equal(primero, await _almacen.ObtenerAsync("K", CancellationToken.None));
    }
}
