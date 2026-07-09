using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Reservas.CrearReserva;
using Reservas.Domain.Puertos;
using Reservas.Domain.Servicios;

namespace Reservas.UnitTests.CrearReserva;

public sealed class CrearReservaCommandHandlerTests
{
    private readonly RepositorioReservasFake _repo = new();
    private readonly DisponibilidadHabitacionFake _dispo = new();
    private readonly PublicadorEventosFake _pub = new();

    private CrearReservaCommandHandler CrearHandler() =>
        new(_dispo, _repo, new CalculadorPrecio(), _pub);

    private static HabitacionReservable HabitacionActiva(Guid id) => new(
        Id: id,
        HotelId: Guid.NewGuid(),
        HotelNombre: "Hotel Central",
        Ciudad: "Medellín",
        Activa: true,
        Capacidad: 2,
        CostoBase: 100.50m,
        Impuesto: 19.99m);

    // AC-E1.6a.4 — confirmación exitosa expone id (UUID v7), estado y precio.
    [Fact]
    public async Task Happy_path_confirma_calcula_precio_persiste_y_publica_evento()
    {
        var comando = DatosPrueba.ComandoValido(); // estancia de 2 noches
        _dispo.Habitacion = HabitacionActiva(comando.HabitacionId);

        var resultado = await CrearHandler().Handle(comando, CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.NotNull(resultado.Valor);
        Assert.NotEqual(Guid.Empty, resultado.Valor!.Id);
        Assert.Equal("Confirmada", resultado.Valor.Estado);
        Assert.Equal(240.98m, resultado.Valor.PrecioTotal); // (100.50 + 19.99) * 2

        var reserva = Assert.Single(_repo.Confirmadas);
        Assert.Equal(resultado.Valor.Id, reserva.Id);

        var publicado = Assert.Single(_pub.Publicados);
        Assert.Equal("reservas", publicado.Topico);
        Assert.Equal(ReservaConfirmadaV1.Tipo, publicado.Evento.Type);
        var data = Assert.IsType<ReservaConfirmadaV1>(publicado.Evento.Data);
        Assert.Equal(resultado.Valor.Id, data.AggregateId);
        Assert.Equal(240.98m, data.PrecioTotal);
        Assert.Equal("juan@correo.com", data.HuespedEmail);
    }

    [Fact]
    public async Task Habitacion_inexistente_devuelve_no_encontrado_sin_persistir_ni_publicar()
    {
        _dispo.Habitacion = null;

        var resultado = await CrearHandler().Handle(DatosPrueba.ComandoValido(), CancellationToken.None);

        Assert.Equal(EstadoResultado.NoEncontrado, resultado.Estado);
        Assert.Empty(_repo.Confirmadas);
        Assert.Empty(_pub.Publicados);
    }

    [Fact]
    public async Task Habitacion_inactiva_devuelve_no_encontrado()
    {
        var comando = DatosPrueba.ComandoValido();
        _dispo.Habitacion = HabitacionActiva(comando.HabitacionId) with { Activa = false };

        var resultado = await CrearHandler().Handle(comando, CancellationToken.None);

        Assert.Equal(EstadoResultado.NoEncontrado, resultado.Estado);
        Assert.Empty(_repo.Confirmadas);
    }

    [Fact]
    public async Task Carrera_perdida_en_el_motor_se_traduce_a_conflicto_y_no_publica()
    {
        var comando = DatosPrueba.ComandoValido();
        _dispo.Habitacion = HabitacionActiva(comando.HabitacionId);
        _repo.SimularNoDisponible = true;

        var resultado = await CrearHandler().Handle(comando, CancellationToken.None);

        Assert.Equal(EstadoResultado.Conflicto, resultado.Estado);
        Assert.Empty(_pub.Publicados); // el evento solo se publica tras confirmar
    }
}
