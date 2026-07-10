using Hoteles.Application.Abstracciones;
using Hoteles.Domain.Hoteles;
using Hoteles.Domain.Puertos;

namespace Hoteles.UnitTests;

/// <summary>
/// <see cref="IContextoAgente"/> fake (Story 6.3): resuelve una identidad fija server-side. Con
/// <see cref="AgenteActual"/> no-nulo, <c>CrearHotelCommandHandler</c> pasa el fail-closed y crea el hotel.
/// </summary>
public sealed class ContextoAgenteFake : IContextoAgente
{
    public string? AgenteActual { get; init; } = "agente@test.com";
}

/// <summary>
/// Fábrica de un <see cref="HotelRepositoryFake"/> que reporta el hotel padre como VISIBLE (Story 6.3), para que
/// los handlers de habitación (que ahora cargan el hotel y devuelven 404 si es null) mantengan verde el
/// happy-path/edición existente.
/// </summary>
public static class HotelesVisiblesFake
{
    public const string Propietario = "agente@test.com";

    public static HotelRepositoryFake ConHotelVisible() => new()
    {
        Existente = Hotel.Crear("Hotel Central", "Medellín", "Calle 1", "Boutique", EstadoHotel.Habilitado, Propietario),
    };
}

/// <summary>
/// Repositorio fake compartido por los slices (alta/edición/baja), sin BD: captura los hoteles creados y
/// permite guionizar la obtención (<see cref="Existente"/> → null simula 404) y el guardado
/// (<see cref="ErrorAlGuardar"/> → simula un conflicto de concurrencia).
/// </summary>
public sealed class HotelRepositoryFake : IHotelRepository
{
    public List<Hotel> Creados { get; } = [];

    /// <summary>Hotel que devolverá <see cref="ObtenerAsync"/> (null → simula «no encontrado»).</summary>
    public Hotel? Existente { get; set; }

    /// <summary>Excepción que lanzará <see cref="GuardarConcurrenciaAsync"/> (p. ej. conflicto de concurrencia).</summary>
    public Exception? ErrorAlGuardar { get; set; }

    public bool GuardoConcurrencia { get; private set; }
    public byte[]? RowVersionRecibido { get; private set; }

    /// <summary>Rowversion que devuelven <see cref="CrearAsync"/>/<see cref="GuardarConcurrenciaAsync"/> (post-save).</summary>
    public byte[] RowVersionResultante { get; set; } = [9, 9, 9, 9];

    public Task<byte[]> CrearAsync(Hotel hotel, CancellationToken ct)
    {
        Creados.Add(hotel);
        return Task.FromResult(RowVersionResultante);
    }

    public Task<Hotel?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(Existente);

    public Task<byte[]> GuardarConcurrenciaAsync(Hotel hotel, byte[] rowVersionOriginal, CancellationToken ct)
    {
        GuardoConcurrencia = true;
        RowVersionRecibido = rowVersionOriginal;
        return ErrorAlGuardar is not null
            ? Task.FromException<byte[]>(ErrorAlGuardar)
            : Task.FromResult(RowVersionResultante);
    }
}
