using HotelBookingHub.Comun.Excepciones;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Hoteles.EliminarHotel;
using Hoteles.Domain.Hoteles;

namespace Hoteles.UnitTests.EliminarHotel;

public sealed class EliminarHotelCommandHandlerTests
{
    private static readonly byte[] _rowVersion = [1, 2, 3, 4, 5, 6, 7, 8];

    private static Hotel HotelExistente() =>
        Hotel.Crear("Hotel Central", "Medellín", "Calle 1 # 2-3", "Boutique", EstadoHotel.Habilitado, "agente@test.com");

    // AC-E2.2.2 — baja lógica: Result.Ok (→ 204) y el hotel queda marcado como eliminado (sin borrado físico).
    [Fact]
    public async Task Happy_path_marca_eliminado_y_devuelve_exito()
    {
        var hotel = HotelExistente();
        var repo = new HotelRepositoryFake { Existente = hotel };

        var resultado = await new EliminarHotelCommandHandler(repo)
            .Handle(new EliminarHotelCommand(hotel.Id, _rowVersion), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.True(hotel.Eliminado);
        Assert.True(repo.GuardoConcurrencia);
        Assert.Equal(_rowVersion, repo.RowVersionRecibido);
    }

    // AC-E2.2.4 — id inexistente (o ya eliminado): 404 y no intenta guardar.
    [Fact]
    public async Task Hotel_inexistente_devuelve_no_encontrado_404()
    {
        var repo = new HotelRepositoryFake { Existente = null };

        var resultado = await new EliminarHotelCommandHandler(repo)
            .Handle(new EliminarHotelCommand(Guid.NewGuid(), _rowVersion), CancellationToken.None);

        Assert.Equal(EstadoResultado.NoEncontrado, resultado.Estado);
        Assert.False(repo.GuardoConcurrencia);
    }

    // AC-E2.2.3 — perder la carrera contra otra baja/edición se propaga como excepción → 409.
    [Fact]
    public async Task Conflicto_de_concurrencia_se_propaga_como_excepcion()
    {
        var hotel = HotelExistente();
        var repo = new HotelRepositoryFake
        {
            Existente = hotel,
            ErrorAlGuardar = new ConflictoConcurrenciaException(new InvalidOperationException()),
        };

        await Assert.ThrowsAsync<ConflictoConcurrenciaException>(
            () => new EliminarHotelCommandHandler(repo).Handle(new EliminarHotelCommand(hotel.Id, _rowVersion), CancellationToken.None));
    }
}
