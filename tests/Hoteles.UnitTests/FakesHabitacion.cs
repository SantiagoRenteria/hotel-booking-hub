using Hoteles.Domain.Habitaciones;
using Hoteles.Domain.Puertos;

namespace Hoteles.UnitTests;

/// <summary>Repositorio fake de habitaciones, sin BD: captura creadas y guioniza obtención/guardado.</summary>
public sealed class HabitacionRepositoryFake : IHabitacionRepository
{
    public List<Habitacion> Creadas { get; } = [];
    public Habitacion? Existente { get; set; }
    public Exception? ErrorAlGuardar { get; set; }
    public bool GuardoConcurrencia { get; private set; }
    public byte[]? RowVersionRecibido { get; private set; }
    public byte[] RowVersionResultante { get; set; } = [9, 9, 9, 9];

    public Task<byte[]> CrearAsync(Habitacion habitacion, CancellationToken ct)
    {
        Creadas.Add(habitacion);
        return Task.FromResult(RowVersionResultante);
    }

    public Task<Habitacion?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(Existente);

    public Task<byte[]> GuardarConcurrenciaAsync(Habitacion habitacion, byte[] rowVersionOriginal, CancellationToken ct)
    {
        GuardoConcurrencia = true;
        RowVersionRecibido = rowVersionOriginal;
        return ErrorAlGuardar is not null
            ? Task.FromException<byte[]>(ErrorAlGuardar)
            : Task.FromResult(RowVersionResultante);
    }
}
