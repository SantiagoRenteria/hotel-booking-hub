using Hoteles.Domain.Hoteles;
using Hoteles.Domain.Puertos;

namespace Hoteles.UnitTests.CrearHotel;

/// <summary>Repositorio fake: captura los hoteles creados, sin BD.</summary>
public sealed class HotelRepositoryFake : IHotelRepository
{
    public List<Hotel> Creados { get; } = [];

    public Task CrearAsync(Hotel hotel, CancellationToken ct)
    {
        Creados.Add(hotel);
        return Task.CompletedTask;
    }
}
