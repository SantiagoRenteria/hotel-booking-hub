using HotelBookingHub.Comun.Eventos;
using Hoteles.Application.Habitaciones.CambiarEstadoHabitacion;
using Hoteles.Application.Habitaciones.CrearHabitacion;
using Hoteles.Application.Habitaciones.EditarHabitacion;
using Hoteles.Domain.Habitaciones;
using Hoteles.Domain.Hoteles;
using Xunit;

namespace Hoteles.UnitTests.Habitaciones;

/// <summary>
/// Story 2.5 (AC-E2.5.2/3/4) — SELECTIVIDAD de la emisión: cada slice de habitación encola el evento de
/// catálogo correcto con su order key, y NO emite cuando no debe (editar sin tocar el precio; habilitar).
/// Aquí solo se verifica QUÉ se encola (con un <see cref="ColaOutboxFake"/>); la atomicidad de que se persista
/// en la MISMA transacción que el dominio se prueba en integración (Testcontainers).
/// </summary>
public sealed class EmisionEventosCatalogoTests
{
    private static readonly byte[] _rowVersion = [1, 2, 3, 4];

    private static Habitacion Existente() =>
        Habitacion.Crear(Guid.CreateVersion7(), "Suite", 100m, 19m, "Piso 3", EstadoHabitacion.Habilitada);

    // ---- CrearHabitacion → HabitacionAgregada.v1 (AC-E2.5.2) ----

    [Fact]
    public async Task Crear_habitacion_emite_HabitacionAgregada_con_version_1()
    {
        var hotel = Hotel.Crear("Hotel Central", "Medellín", "Calle 1", "Boutique", EstadoHotel.Habilitado);
        var hoteles = new HotelRepositoryFake { Existente = hotel };
        var habitaciones = new HabitacionRepositoryFake();
        var outbox = new ColaOutboxFake();
        var comando = new CrearHabitacionCommand(hotel.Id, "Suite", 100m, 19m, "Piso 3", EstadoHabitacion.Habilitada);

        await new CrearHabitacionCommandHandler(hoteles, habitaciones, outbox).Handle(comando, CancellationToken.None);

        var encolado = Assert.Single(outbox.Encolados);
        Assert.Equal(HabitacionAgregadaV1.Tipo, encolado.Tipo);
        Assert.Equal(1, encolado.Version);
        var creada = Assert.Single(habitaciones.Creadas);
        Assert.Equal(creada.Id, encolado.AggregateId); // order key = HabitacionId
        var data = Assert.IsType<HabitacionAgregadaV1>(encolado.Data);
        Assert.Equal(creada.Id, data.AggregateId);
        Assert.Equal(hotel.Id, data.HotelId);
        Assert.Equal("Suite", data.TipoHabitacion);
        Assert.Equal(100m, data.CostoBase);
    }

    [Fact]
    public async Task Crear_habitacion_sobre_hotel_inexistente_no_emite_nada()
    {
        var hoteles = new HotelRepositoryFake { Existente = null };
        var outbox = new ColaOutboxFake();
        var comando = new CrearHabitacionCommand(Guid.NewGuid(), "Suite", 100m, 19m, "Piso 3", EstadoHabitacion.Habilitada);

        await new CrearHabitacionCommandHandler(hoteles, new HabitacionRepositoryFake(), outbox).Handle(comando, CancellationToken.None);

        Assert.Empty(outbox.Encolados);
    }

    // ---- EditarHabitacion → PrecioHabitacionCambiado.v1 SOLO si cambia el precio (AC-E2.5.3) ----

    [Fact]
    public async Task Editar_cambiando_el_precio_emite_PrecioHabitacionCambiado_con_los_montos_nuevos()
    {
        var habitacion = Existente();
        var repo = new HabitacionRepositoryFake { Existente = habitacion };
        var outbox = new ColaOutboxFake();
        var comando = new EditarHabitacionCommand(habitacion.Id, _rowVersion, "Suite Premium", 150m, 28.5m, "Piso 5");

        await new EditarHabitacionCommandHandler(repo, outbox).Handle(comando, CancellationToken.None);

        var encolado = Assert.Single(outbox.Encolados);
        Assert.Equal(PrecioHabitacionCambiadoV1.Tipo, encolado.Tipo);
        Assert.Equal(2, encolado.Version);
        Assert.Equal(habitacion.Id, encolado.AggregateId);
        var data = Assert.IsType<PrecioHabitacionCambiadoV1>(encolado.Data);
        Assert.Equal(150m, data.CostoBase);
        Assert.Equal(28.5m, data.Impuestos);
    }

    [Fact]
    public async Task Editar_sin_cambiar_el_precio_no_emite_evento_de_precio()
    {
        var habitacion = Existente();
        var repo = new HabitacionRepositoryFake { Existente = habitacion };
        var outbox = new ColaOutboxFake();
        // Cambia tipo/ubicación pero deja costoBase/impuestos idénticos.
        var comando = new EditarHabitacionCommand(habitacion.Id, _rowVersion, "Suite Deluxe", 100m, 19m, "Piso 9");

        await new EditarHabitacionCommandHandler(repo, outbox).Handle(comando, CancellationToken.None);

        Assert.Empty(outbox.Encolados);
    }

    // ---- CambiarEstadoHabitacion → HabitacionDeshabilitada.v1 solo al deshabilitar (AC-E2.5.3) ----

    [Fact]
    public async Task Deshabilitar_emite_HabitacionDeshabilitada()
    {
        var habitacion = Existente();
        var repo = new HabitacionRepositoryFake { Existente = habitacion };
        var outbox = new ColaOutboxFake();
        var comando = new CambiarEstadoHabitacionCommand(habitacion.Id, _rowVersion, EstadoHabitacion.Deshabilitada);

        await new CambiarEstadoHabitacionCommandHandler(repo, outbox).Handle(comando, CancellationToken.None);

        var encolado = Assert.Single(outbox.Encolados);
        Assert.Equal(HabitacionDeshabilitadaV1.Tipo, encolado.Tipo);
        Assert.Equal(2, encolado.Version);
        Assert.Equal(habitacion.Id, encolado.AggregateId);
        Assert.IsType<HabitacionDeshabilitadaV1>(encolado.Data);
    }

    [Fact]
    public async Task Habilitar_no_emite_ningun_evento()
    {
        var habitacion = Existente();
        habitacion.Deshabilitar();
        var repo = new HabitacionRepositoryFake { Existente = habitacion };
        var outbox = new ColaOutboxFake();
        var comando = new CambiarEstadoHabitacionCommand(habitacion.Id, _rowVersion, EstadoHabitacion.Habilitada);

        await new CambiarEstadoHabitacionCommandHandler(repo, outbox).Handle(comando, CancellationToken.None);

        Assert.Empty(outbox.Encolados);
    }

    [Fact]
    public async Task Deshabilitar_una_ya_deshabilitada_no_emite_evento()
    {
        var habitacion = Existente();
        habitacion.Deshabilitar();
        var repo = new HabitacionRepositoryFake { Existente = habitacion };
        var outbox = new ColaOutboxFake();
        var comando = new CambiarEstadoHabitacionCommand(habitacion.Id, _rowVersion, EstadoHabitacion.Deshabilitada);

        await new CambiarEstadoHabitacionCommandHandler(repo, outbox).Handle(comando, CancellationToken.None);

        Assert.Empty(outbox.Encolados);
    }
}
