using Hoteles.Application.Hoteles.CrearHotel;
using Hoteles.Domain.Hoteles;

namespace Hoteles.UnitTests.CrearHotel;

public sealed class CrearHotelCommandHandlerTests
{
    // AC-E2.1.1 — alta válida: 201 con Hotel (UUID v7) en el estado indicado.
    [Fact]
    public async Task Happy_path_crea_hotel_con_id_v7_y_estado()
    {
        var repo = new HotelRepositoryFake { RowVersionResultante = [1, 2, 3, 4] };
        var comando = new CrearHotelCommand("Hotel Central", "Medellín", "Calle 1 # 2-3", "Boutique", EstadoHotel.Habilitado);

        var resultado = await new CrearHotelCommandHandler(repo).Handle(comando, CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.NotNull(resultado.Valor);
        Assert.NotEqual(Guid.Empty, resultado.Valor!.Id);
        Assert.Equal("Hotel Central", resultado.Valor.Nombre);
        Assert.Equal("Medellín", resultado.Valor.Ciudad);
        Assert.Equal("Habilitado", resultado.Valor.Estado);
        Assert.Equal(Convert.ToBase64String([1, 2, 3, 4]), resultado.Valor.RowVersion); // rowVersion inicial expuesto

        var hotel = Assert.Single(repo.Creados);
        Assert.Equal(resultado.Valor.Id, hotel.Id);
        Assert.Equal(EstadoHotel.Habilitado, hotel.Estado);
    }

    [Fact]
    public async Task Respeta_el_estado_deshabilitado_indicado()
    {
        var repo = new HotelRepositoryFake();
        var comando = new CrearHotelCommand("Hotel Norte", "Cali", "Av 2", "", EstadoHotel.Deshabilitado);

        var resultado = await new CrearHotelCommandHandler(repo).Handle(comando, CancellationToken.None);

        Assert.Equal("Deshabilitado", resultado.Valor!.Estado);
    }
}
