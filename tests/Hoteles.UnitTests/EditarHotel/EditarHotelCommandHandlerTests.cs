using HotelBookingHub.Comun.Excepciones;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Hoteles.EditarHotel;
using Hoteles.Domain.Hoteles;

namespace Hoteles.UnitTests.EditarHotel;

public sealed class EditarHotelCommandHandlerTests
{
    private static readonly byte[] _rowVersion = [1, 2, 3, 4, 5, 6, 7, 8];

    private static Hotel HotelExistente() =>
        Hotel.Crear("Hotel Central", "Medellín", "Calle 1 # 2-3", "Boutique", EstadoHotel.Habilitado);

    private static EditarHotelCommand Comando(Guid id) =>
        new(id, _rowVersion, "Hotel Renovado", "Cali", "Av 3", "Actualizado");

    // AC-E2.2.1 — edición válida: 200 con los datos actualizados; el rowVersion del cliente llega al repo y el
    // nuevo rowVersion vuelve en la respuesta. El Estado NO cambia por edición (se reserva a 2.3).
    [Fact]
    public async Task Happy_path_edita_y_devuelve_200_con_datos_actualizados()
    {
        var hotel = HotelExistente();
        var repo = new HotelRepositoryFake { Existente = hotel, RowVersionResultante = [7, 7, 7, 7] };

        var resultado = await new EditarHotelCommandHandler(repo).Handle(Comando(hotel.Id), CancellationToken.None);

        Assert.True(resultado.EsExitoso);
        Assert.Equal("Hotel Renovado", resultado.Valor!.Nombre);
        Assert.Equal("Cali", resultado.Valor.Ciudad);
        Assert.Equal("Habilitado", resultado.Valor.Estado); // la edición no altera el ciclo de vida
        Assert.Equal(Convert.ToBase64String([7, 7, 7, 7]), resultado.Valor.RowVersion); // nuevo token en la respuesta
        Assert.True(repo.GuardoConcurrencia);
        Assert.Equal(_rowVersion, repo.RowVersionRecibido); // el token del cliente se propaga al repositorio
    }

    // AC-E2.2.4 — id inexistente (o ya eliminado): 404 y no intenta guardar.
    [Fact]
    public async Task Hotel_inexistente_devuelve_no_encontrado_404()
    {
        var repo = new HotelRepositoryFake { Existente = null };

        var resultado = await new EditarHotelCommandHandler(repo).Handle(Comando(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(EstadoResultado.NoEncontrado, resultado.Estado);
        Assert.False(repo.GuardoConcurrencia);
    }

    // AC-E2.2.3 — el conflicto de concurrencia NO es un Result: se propaga como excepción que el handler
    // transversal traduce a 409 (probado en Comun.Web.UnitTests).
    [Fact]
    public async Task Conflicto_de_concurrencia_se_propaga_como_excepcion()
    {
        var hotel = HotelExistente();
        var repo = new HotelRepositoryFake
        {
            Existente = hotel,
            ErrorAlGuardar = new ConflictoConcurrenciaException(new InvalidOperationException()),
        };

        await Assert.ThrowsAsync<ConflictoConcurrenciaException>(
            () => new EditarHotelCommandHandler(repo).Handle(Comando(hotel.Id), CancellationToken.None));
    }
}
