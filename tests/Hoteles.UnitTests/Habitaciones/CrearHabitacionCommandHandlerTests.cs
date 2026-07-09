using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Habitaciones.CrearHabitacion;
using Hoteles.Domain.Habitaciones;
using Hoteles.Domain.Hoteles;

namespace Hoteles.UnitTests.Habitaciones;

public sealed class CrearHabitacionCommandHandlerTests
{
    private static Hotel UnHotel() =>
        Hotel.Crear("Hotel Central", "Medellín", "Calle 1", "Boutique", EstadoHotel.Habilitado);

    private static CrearHabitacionCommand Comando(Guid hotelId) =>
        new(hotelId, "Suite", 100.00m, 19.00m, "Piso 3", EstadoHabitacion.Habilitada);

    // AC-E2.4.1 — alta válida sobre hotel existente: 201 con la habitación (rowVersion incluido).
    [Fact]
    public async Task Happy_path_crea_habitacion_en_hotel_existente()
    {
        var hotel = UnHotel();
        var hoteles = new HotelRepositoryFake { Existente = hotel };
        var habitaciones = new HabitacionRepositoryFake { RowVersionResultante = [1, 2, 3, 4] };

        var resultado = await new CrearHabitacionCommandHandler(hoteles, habitaciones)
            .Handle(Comando(hotel.Id), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal(hotel.Id, resultado.Valor!.HotelId);
        Assert.Equal("Suite", resultado.Valor.Tipo);
        Assert.Equal(100.00m, resultado.Valor.CostoBase);
        Assert.Equal("Habilitada", resultado.Valor.Estado);
        Assert.Equal(Convert.ToBase64String([1, 2, 3, 4]), resultado.Valor.RowVersion);
        Assert.Single(habitaciones.Creadas);
    }

    // AC-E2.4.1 — hotel inexistente (o eliminado): 404 y no crea nada.
    [Fact]
    public async Task Hotel_inexistente_devuelve_404()
    {
        var hoteles = new HotelRepositoryFake { Existente = null };
        var habitaciones = new HabitacionRepositoryFake();

        var resultado = await new CrearHabitacionCommandHandler(hoteles, habitaciones)
            .Handle(Comando(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(EstadoResultado.NoEncontrado, resultado.Estado);
        Assert.Empty(habitaciones.Creadas);
    }
}
