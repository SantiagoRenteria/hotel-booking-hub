using HotelBookingHub.Comun.Excepciones;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Habitaciones.EditarHabitacion;
using Hoteles.Domain.Habitaciones;

namespace Hoteles.UnitTests.Habitaciones;

public sealed class EditarHabitacionCommandHandlerTests
{
    private static readonly byte[] _rowVersion = [1, 2, 3, 4, 5, 6, 7, 8];

    private static Habitacion UnaHabitacion() =>
        Habitacion.Crear(Guid.CreateVersion7(), "Suite", 100m, 19m, "Piso 3", EstadoHabitacion.Habilitada);

    private static EditarHabitacionCommand Comando(Guid id) =>
        new(id, _rowVersion, "Suite Premium", 150m, 28.5m, "Piso 5");

    // AC-E2.4.2 — edición válida: 200 con datos nuevos; el estado NO cambia por edición.
    [Fact]
    public async Task Happy_path_edita_y_devuelve_200()
    {
        var habitacion = UnaHabitacion();
        var repo = new HabitacionRepositoryFake { Existente = habitacion, RowVersionResultante = [7, 7, 7, 7] };

        var resultado = await new EditarHabitacionCommandHandler(repo).Handle(Comando(habitacion.Id), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal("Suite Premium", resultado.Valor!.Tipo);
        Assert.Equal(150m, resultado.Valor.CostoBase);
        Assert.Equal("Habilitada", resultado.Valor.Estado); // la edición no toca el estado
        Assert.Equal(Convert.ToBase64String([7, 7, 7, 7]), resultado.Valor.RowVersion);
        Assert.Equal(_rowVersion, repo.RowVersionRecibido);
    }

    [Fact]
    public async Task Habitacion_inexistente_devuelve_404()
    {
        var repo = new HabitacionRepositoryFake { Existente = null };

        var resultado = await new EditarHabitacionCommandHandler(repo).Handle(Comando(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(EstadoResultado.NoEncontrado, resultado.Estado);
        Assert.False(repo.GuardoConcurrencia);
    }

    [Fact]
    public async Task Conflicto_de_concurrencia_se_propaga()
    {
        var habitacion = UnaHabitacion();
        var repo = new HabitacionRepositoryFake
        {
            Existente = habitacion,
            ErrorAlGuardar = new ConflictoConcurrenciaException(new InvalidOperationException()),
        };

        await Assert.ThrowsAsync<ConflictoConcurrenciaException>(
            () => new EditarHabitacionCommandHandler(repo).Handle(Comando(habitacion.Id), CancellationToken.None));
    }
}
