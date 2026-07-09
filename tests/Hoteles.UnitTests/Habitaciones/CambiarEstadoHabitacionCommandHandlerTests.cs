using HotelBookingHub.Comun.Excepciones;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Habitaciones.CambiarEstadoHabitacion;
using Hoteles.Domain.Habitaciones;

namespace Hoteles.UnitTests.Habitaciones;

public sealed class CambiarEstadoHabitacionCommandHandlerTests
{
    private static readonly byte[] _rowVersion = [1, 2, 3, 4];

    private static Habitacion UnaHabitacion(EstadoHabitacion estado)
    {
        var h = Habitacion.Crear(Guid.CreateVersion7(), "Suite", 100m, 19m, "Piso 3", EstadoHabitacion.Habilitada, capacidad: 2);
        if (estado == EstadoHabitacion.Deshabilitada)
        {
            h.Deshabilitar();
        }

        return h;
    }

    private static CambiarEstadoHabitacionCommand Comando(Guid id, EstadoHabitacion objetivo) => new(id, _rowVersion, objetivo);

    // AC-E2.4.3 — deshabilitar: 200 + estado Deshabilitada.
    [Fact]
    public async Task Deshabilitar_cambia_estado_y_devuelve_200()
    {
        var habitacion = UnaHabitacion(EstadoHabitacion.Habilitada);
        var repo = new HabitacionRepositoryFake { Existente = habitacion };

        var resultado = await new CambiarEstadoHabitacionCommandHandler(repo, new ColaOutboxFake())
            .Handle(Comando(habitacion.Id, EstadoHabitacion.Deshabilitada), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal("Deshabilitada", resultado.Valor!.Estado);
        Assert.Equal(EstadoHabitacion.Deshabilitada, habitacion.Estado);
    }

    // AC-E2.4.3 — habilitar una deshabilitada: 200 + estado Habilitada.
    [Fact]
    public async Task Habilitar_una_deshabilitada_devuelve_200_y_habilitada()
    {
        var habitacion = UnaHabitacion(EstadoHabitacion.Deshabilitada);
        var repo = new HabitacionRepositoryFake { Existente = habitacion };

        var resultado = await new CambiarEstadoHabitacionCommandHandler(repo, new ColaOutboxFake())
            .Handle(Comando(habitacion.Id, EstadoHabitacion.Habilitada), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal("Habilitada", resultado.Valor!.Estado);
        Assert.Equal(EstadoHabitacion.Habilitada, habitacion.Estado);
    }

    // AC-E2.4.3 — idempotencia: deshabilitar una ya deshabilitada no es error.
    [Fact]
    public async Task Deshabilitar_una_ya_deshabilitada_es_idempotente()
    {
        var habitacion = UnaHabitacion(EstadoHabitacion.Deshabilitada);
        var repo = new HabitacionRepositoryFake { Existente = habitacion };

        var resultado = await new CambiarEstadoHabitacionCommandHandler(repo, new ColaOutboxFake())
            .Handle(Comando(habitacion.Id, EstadoHabitacion.Deshabilitada), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal(EstadoHabitacion.Deshabilitada, habitacion.Estado);
        Assert.True(repo.GuardoConcurrencia);
    }

    // AC-E2.4.3 — matriz de transiciones (party-mode · Murat): toda combinación origen→objetivo, incluidas las
    // idempotentes, termina en el estado pedido. Unificar la transición concentra el riesgo en un solo método.
    [Theory]
    [InlineData(EstadoHabitacion.Habilitada, EstadoHabitacion.Habilitada)]
    [InlineData(EstadoHabitacion.Habilitada, EstadoHabitacion.Deshabilitada)]
    [InlineData(EstadoHabitacion.Deshabilitada, EstadoHabitacion.Habilitada)]
    [InlineData(EstadoHabitacion.Deshabilitada, EstadoHabitacion.Deshabilitada)]
    public async Task Matriz_de_transiciones_termina_en_el_estado_objetivo(EstadoHabitacion origen, EstadoHabitacion objetivo)
    {
        var habitacion = UnaHabitacion(origen);
        var repo = new HabitacionRepositoryFake { Existente = habitacion };

        var resultado = await new CambiarEstadoHabitacionCommandHandler(repo, new ColaOutboxFake())
            .Handle(Comando(habitacion.Id, objetivo), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal(objetivo, habitacion.Estado);
        Assert.Equal(objetivo.ToString(), resultado.Valor!.Estado);
        Assert.True(repo.GuardoConcurrencia);
    }

    [Fact]
    public async Task Habitacion_inexistente_devuelve_404()
    {
        var repo = new HabitacionRepositoryFake { Existente = null };

        var resultado = await new CambiarEstadoHabitacionCommandHandler(repo, new ColaOutboxFake())
            .Handle(Comando(Guid.NewGuid(), EstadoHabitacion.Habilitada), CancellationToken.None);

        Assert.Equal(EstadoResultado.NoEncontrado, resultado.Estado);
        Assert.False(repo.GuardoConcurrencia);
    }

    [Fact]
    public async Task Conflicto_de_concurrencia_se_propaga()
    {
        var habitacion = UnaHabitacion(EstadoHabitacion.Habilitada);
        var repo = new HabitacionRepositoryFake
        {
            Existente = habitacion,
            ErrorAlGuardar = new ConflictoConcurrenciaException(new InvalidOperationException()),
        };

        await Assert.ThrowsAsync<ConflictoConcurrenciaException>(
            () => new CambiarEstadoHabitacionCommandHandler(repo, new ColaOutboxFake()).Handle(Comando(habitacion.Id, EstadoHabitacion.Deshabilitada), CancellationToken.None));
    }
}
