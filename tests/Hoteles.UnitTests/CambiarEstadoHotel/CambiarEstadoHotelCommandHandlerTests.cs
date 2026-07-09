using HotelBookingHub.Comun.Excepciones;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Hoteles.CambiarEstadoHotel;
using Hoteles.Domain.Hoteles;

namespace Hoteles.UnitTests.CambiarEstadoHotel;

public sealed class CambiarEstadoHotelCommandHandlerTests
{
    private static readonly byte[] _rowVersion = [1, 2, 3, 4, 5, 6, 7, 8];

    private static Hotel HotelEn(EstadoHotel estado)
    {
        var hotel = Hotel.Crear("Hotel Central", "Medellín", "Calle 1", "Boutique", EstadoHotel.Habilitado);
        if (estado == EstadoHotel.Deshabilitado)
        {
            hotel.Deshabilitar();
        }

        return hotel;
    }

    private static CambiarEstadoHotelCommand Comando(Guid id, EstadoHotel objetivo) => new(id, _rowVersion, objetivo);

    // AC-E2.3.1 — deshabilitar: 200, estado Deshabilitado y nuevo rowVersion; token del cliente propagado.
    [Fact]
    public async Task Deshabilitar_cambia_estado_y_devuelve_200()
    {
        var hotel = HotelEn(EstadoHotel.Habilitado);
        var repo = new HotelRepositoryFake { Existente = hotel, RowVersionResultante = [7, 7, 7, 7] };

        var resultado = await new CambiarEstadoHotelCommandHandler(repo)
            .Handle(Comando(hotel.Id, EstadoHotel.Deshabilitado), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal("Deshabilitado", resultado.Valor!.Estado);
        Assert.Equal(EstadoHotel.Deshabilitado, hotel.Estado);
        Assert.Equal(Convert.ToBase64String([7, 7, 7, 7]), resultado.Valor.RowVersion);
        Assert.Equal(_rowVersion, repo.RowVersionRecibido);
    }

    // AC-E2.3.1 — habilitar de vuelta.
    [Fact]
    public async Task Habilitar_cambia_estado_y_devuelve_200()
    {
        var hotel = HotelEn(EstadoHotel.Deshabilitado);
        var repo = new HotelRepositoryFake { Existente = hotel };

        var resultado = await new CambiarEstadoHotelCommandHandler(repo)
            .Handle(Comando(hotel.Id, EstadoHotel.Habilitado), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal("Habilitado", resultado.Valor!.Estado);
        Assert.Equal(EstadoHotel.Habilitado, hotel.Estado);
    }

    // AC-E2.3.2 — idempotencia: deshabilitar un ya deshabilitado no es error (estado final = el pedido).
    [Fact]
    public async Task Deshabilitar_un_ya_deshabilitado_es_idempotente()
    {
        var hotel = HotelEn(EstadoHotel.Deshabilitado);
        var repo = new HotelRepositoryFake { Existente = hotel };

        var resultado = await new CambiarEstadoHotelCommandHandler(repo)
            .Handle(Comando(hotel.Id, EstadoHotel.Deshabilitado), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal(EstadoHotel.Deshabilitado, hotel.Estado);
        Assert.True(repo.GuardoConcurrencia); // igual arbitra la concurrencia (fuerza el UPDATE)
    }

    // AC-E2.3.4 — inexistente o eliminado: 404, sin guardar.
    [Fact]
    public async Task Hotel_inexistente_devuelve_404()
    {
        var repo = new HotelRepositoryFake { Existente = null };

        var resultado = await new CambiarEstadoHotelCommandHandler(repo)
            .Handle(Comando(Guid.NewGuid(), EstadoHotel.Deshabilitado), CancellationToken.None);

        Assert.Equal(EstadoResultado.NoEncontrado, resultado.Estado);
        Assert.False(repo.GuardoConcurrencia);
    }

    // AC-E2.3.3 — conflicto de concurrencia se propaga (→ 409 por el handler transversal).
    [Fact]
    public async Task Conflicto_de_concurrencia_se_propaga_como_excepcion()
    {
        var hotel = HotelEn(EstadoHotel.Habilitado);
        var repo = new HotelRepositoryFake
        {
            Existente = hotel,
            ErrorAlGuardar = new ConflictoConcurrenciaException(new InvalidOperationException()),
        };

        await Assert.ThrowsAsync<ConflictoConcurrenciaException>(
            () => new CambiarEstadoHotelCommandHandler(repo).Handle(Comando(hotel.Id, EstadoHotel.Deshabilitado), CancellationToken.None));
    }
}
