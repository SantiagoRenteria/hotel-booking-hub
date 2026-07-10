using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Excepciones;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Hoteles.CambiarEstadoHotel;
using Hoteles.Domain.Hoteles;

namespace Hoteles.UnitTests.CambiarEstadoHotel;

public sealed class CambiarEstadoHotelCommandHandlerTests
{
    private static readonly byte[] _rowVersion = [1, 2, 3, 4, 5, 6, 7, 8];

    private static Hotel HotelEn(EstadoHotel estado)
    {
        var hotel = Hotel.Crear("Hotel Central", "Medellín", "Calle 1", "Boutique", EstadoHotel.Habilitado, "agente@test.com");
        if (estado == EstadoHotel.Deshabilitado)
        {
            hotel.Deshabilitar();
        }

        return hotel;
    }

    private static CambiarEstadoHotelCommand Comando(Guid id, EstadoHotel objetivo) => new(id, _rowVersion, objetivo);

    private static CambiarEstadoHotelCommandHandler Handler(HotelRepositoryFake repo) =>
        new(repo, new ColaOutboxFake());

    // AC-E2.3.1 — deshabilitar: 200, estado Deshabilitado y nuevo rowVersion; token del cliente propagado.
    [Fact]
    public async Task Deshabilitar_cambia_estado_y_devuelve_200()
    {
        var hotel = HotelEn(EstadoHotel.Habilitado);
        var repo = new HotelRepositoryFake { Existente = hotel, RowVersionResultante = [7, 7, 7, 7] };

        var resultado = await Handler(repo)
            .Handle(Comando(hotel.Id, EstadoHotel.Deshabilitado), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal("Deshabilitado", resultado.Valor!.Estado);
        Assert.Equal(EstadoHotel.Deshabilitado, hotel.Estado);
        Assert.Equal(Convert.ToBase64String([7, 7, 7, 7]), resultado.Valor.RowVersion);
        Assert.Equal(_rowVersion, repo.RowVersionRecibido);
    }

    // AC-E2.3.1 — habilitar de vuelta.
    [Fact]
    public async Task Habilitar_cambia_estado_y_devuelve_200()
    {
        var hotel = HotelEn(EstadoHotel.Deshabilitado);
        var repo = new HotelRepositoryFake { Existente = hotel };

        var resultado = await Handler(repo)
            .Handle(Comando(hotel.Id, EstadoHotel.Habilitado), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal("Habilitado", resultado.Valor!.Estado);
        Assert.Equal(EstadoHotel.Habilitado, hotel.Estado);
    }

    // AC-E2.3.2 — idempotencia: deshabilitar un ya deshabilitado no es error (estado final = el pedido).
    [Fact]
    public async Task Deshabilitar_un_ya_deshabilitado_es_idempotente()
    {
        var hotel = HotelEn(EstadoHotel.Deshabilitado);
        var repo = new HotelRepositoryFake { Existente = hotel };

        var resultado = await Handler(repo)
            .Handle(Comando(hotel.Id, EstadoHotel.Deshabilitado), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal(EstadoHotel.Deshabilitado, hotel.Estado);
        Assert.True(repo.GuardoConcurrencia); // igual arbitra la concurrencia (fuerza el UPDATE)
    }

    // Story 3.2 (AC-E3.2.2) — deshabilitar EFECTIVO emite HotelDeshabilitado.v1 con la Version como order key.
    [Fact]
    public async Task Deshabilitar_efectivo_emite_HotelDeshabilitado()
    {
        var hotel = HotelEn(EstadoHotel.Habilitado);
        var repo = new HotelRepositoryFake { Existente = hotel };
        var outbox = new ColaOutboxFake();

        await new CambiarEstadoHotelCommandHandler(repo, outbox)
            .Handle(Comando(hotel.Id, EstadoHotel.Deshabilitado), CancellationToken.None);

        var e = Assert.Single(outbox.Encolados);
        Assert.Equal(HotelDeshabilitadoV1.Tipo, e.Tipo);
        Assert.Equal(hotel.Id, e.AggregateId);
        Assert.Equal(hotel.Version, e.Version);
        Assert.Equal(new HotelDeshabilitadoV1(hotel.Id, hotel.Ciudad), e.Data);
    }

    // Story 3.2 — habilitar EFECTIVO emite HotelHabilitado.v1.
    [Fact]
    public async Task Habilitar_efectivo_emite_HotelHabilitado()
    {
        var hotel = HotelEn(EstadoHotel.Deshabilitado);
        var repo = new HotelRepositoryFake { Existente = hotel };
        var outbox = new ColaOutboxFake();

        await new CambiarEstadoHotelCommandHandler(repo, outbox)
            .Handle(Comando(hotel.Id, EstadoHotel.Habilitado), CancellationToken.None);

        var e = Assert.Single(outbox.Encolados);
        Assert.Equal(HotelHabilitadoV1.Tipo, e.Tipo);
        Assert.Equal(hotel.Version, e.Version);
    }

    // Story 3.2 — la transición idempotente NO emite (selectividad, como en habitaciones AC-E2.5.3).
    [Theory]
    [InlineData(EstadoHotel.Habilitado, EstadoHotel.Habilitado)]
    [InlineData(EstadoHotel.Deshabilitado, EstadoHotel.Deshabilitado)]
    public async Task Transicion_idempotente_no_emite(EstadoHotel origen, EstadoHotel objetivo)
    {
        var hotel = HotelEn(origen);
        var repo = new HotelRepositoryFake { Existente = hotel };
        var outbox = new ColaOutboxFake();

        await new CambiarEstadoHotelCommandHandler(repo, outbox)
            .Handle(Comando(hotel.Id, objetivo), CancellationToken.None);

        Assert.Empty(outbox.Encolados);
    }

    // AC-E2.3.1/2 — matriz de transiciones (party-mode · Murat): toda combinación origen→objetivo, incluidas
    // las idempotentes (habilitar un habilitado / deshabilitar un deshabilitado), termina en el estado pedido.
    [Theory]
    [InlineData(EstadoHotel.Habilitado, EstadoHotel.Habilitado)]
    [InlineData(EstadoHotel.Habilitado, EstadoHotel.Deshabilitado)]
    [InlineData(EstadoHotel.Deshabilitado, EstadoHotel.Habilitado)]
    [InlineData(EstadoHotel.Deshabilitado, EstadoHotel.Deshabilitado)]
    public async Task Matriz_de_transiciones_termina_en_el_estado_objetivo(EstadoHotel origen, EstadoHotel objetivo)
    {
        var hotel = HotelEn(origen);
        var repo = new HotelRepositoryFake { Existente = hotel };

        var resultado = await Handler(repo)
            .Handle(Comando(hotel.Id, objetivo), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal(objetivo, hotel.Estado);
        Assert.Equal(objetivo.ToString(), resultado.Valor!.Estado);
        Assert.True(repo.GuardoConcurrencia); // incluso la transición idempotente arbitra el token
    }

    // AC-E2.3.4 — inexistente o eliminado: 404, sin guardar.
    [Fact]
    public async Task Hotel_inexistente_devuelve_404()
    {
        var repo = new HotelRepositoryFake { Existente = null };

        var resultado = await Handler(repo)
            .Handle(Comando(Guid.NewGuid(), EstadoHotel.Deshabilitado), CancellationToken.None);

        Assert.Equal(EstadoResultado.NoEncontrado, resultado.Estado);
        Assert.False(repo.GuardoConcurrencia);
    }

    // AC-E2.3.3 — conflicto de concurrencia se propaga (→ 409 por el handler transversal).
    [Fact]
    public async Task Conflicto_de_concurrencia_se_propaga_como_excepcion()
    {
        var hotel = HotelEn(EstadoHotel.Habilitado);
        var repo = new HotelRepositoryFake
        {
            Existente = hotel,
            ErrorAlGuardar = new ConflictoConcurrenciaException(new InvalidOperationException()),
        };

        await Assert.ThrowsAsync<ConflictoConcurrenciaException>(
            () => Handler(repo).Handle(Comando(hotel.Id, EstadoHotel.Deshabilitado), CancellationToken.None));
    }
}
