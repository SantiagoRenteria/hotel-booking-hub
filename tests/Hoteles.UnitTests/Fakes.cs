using Hoteles.Domain.Hoteles;
using Hoteles.Domain.Puertos;

namespace Hoteles.UnitTests;

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
