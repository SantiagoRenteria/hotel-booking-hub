using FluentValidation.TestHelper;
using Hoteles.Application.Habitaciones.CambiarEstadoHabitacion;
using Hoteles.Application.Habitaciones.CrearHabitacion;
using Hoteles.Application.Habitaciones.EditarHabitacion;
using Hoteles.Domain.Habitaciones;

namespace Hoteles.UnitTests.Habitaciones;

public sealed class HabitacionValidatorsTests
{
    private readonly CrearHabitacionCommandValidator _crear = new();
    private readonly EditarHabitacionCommandValidator _editar = new();
    private readonly CambiarEstadoHabitacionCommandValidator _estado = new();

    [Fact]
    public void Crear_valido_no_tiene_errores()
    {
        var c = new CrearHabitacionCommand(Guid.CreateVersion7(), "Suite", 100m, 19m, "Piso 3", EstadoHabitacion.Habilitada, Capacidad: 2);
        _crear.TestValidate(c).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Crear_sin_hotel_es_invalido()
    {
        var c = new CrearHabitacionCommand(Guid.Empty, "Suite", 100m, 19m, "Piso 3", EstadoHabitacion.Habilitada, Capacidad: 2);
        _crear.TestValidate(c).ShouldHaveValidationErrorFor(x => x.HotelId);
    }

    [Fact]
    public void Crear_con_costo_negativo_es_invalido()
    {
        var c = new CrearHabitacionCommand(Guid.CreateVersion7(), "Suite", -1m, 19m, "Piso 3", EstadoHabitacion.Habilitada, Capacidad: 2);
        _crear.TestValidate(c).ShouldHaveValidationErrorFor(x => x.CostoBase);
    }

    [Fact]
    public void Crear_con_tipo_sobredimensionado_es_invalido()
    {
        var c = new CrearHabitacionCommand(Guid.CreateVersion7(), new string('a', LongitudesHabitacion.Tipo + 1), 100m, 19m, "Piso 3", EstadoHabitacion.Habilitada, Capacidad: 2);
        _crear.TestValidate(c).ShouldHaveValidationErrorFor(x => x.Tipo);
    }

    // Un monto que no cabe en decimal(18,2) debe ser 400 (validación), no 500 por overflow en el INSERT.
    [Fact]
    public void Crear_con_monto_que_excede_la_precision_es_invalido()
    {
        var c = new CrearHabitacionCommand(Guid.CreateVersion7(), "Suite", 100000000000000000m, 19m, "Piso 3", EstadoHabitacion.Habilitada, Capacidad: 2);
        _crear.TestValidate(c).ShouldHaveValidationErrorFor(x => x.CostoBase);
    }

    // Más de 2 decimales debe rechazarse (evita el redondeo silencioso respuesta≠persistido).
    [Fact]
    public void Crear_con_mas_de_dos_decimales_es_invalido()
    {
        var c = new CrearHabitacionCommand(Guid.CreateVersion7(), "Suite", 100.999m, 19m, "Piso 3", EstadoHabitacion.Habilitada, Capacidad: 2);
        _crear.TestValidate(c).ShouldHaveValidationErrorFor(x => x.CostoBase);
    }

    // Capacidad debe ser > 0 (la búsqueda 3.2 filtra por capacidad; un valor no positivo no tiene sentido).
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Crear_con_capacidad_no_positiva_es_invalido(int capacidad)
    {
        var c = new CrearHabitacionCommand(Guid.CreateVersion7(), "Suite", 100m, 19m, "Piso 3", EstadoHabitacion.Habilitada, capacidad);
        _crear.TestValidate(c).ShouldHaveValidationErrorFor(x => x.Capacidad);
    }

    [Fact]
    public void Editar_sin_rowversion_es_invalido()
    {
        var c = new EditarHabitacionCommand(Guid.CreateVersion7(), [], "Suite", 100m, 19m, "Piso 3", Capacidad: 2);
        _editar.TestValidate(c).ShouldHaveValidationErrorFor(x => x.RowVersion);
    }

    [Fact]
    public void Editar_con_impuestos_negativos_es_invalido()
    {
        var c = new EditarHabitacionCommand(Guid.CreateVersion7(), [1], "Suite", 100m, -5m, "Piso 3", Capacidad: 2);
        _editar.TestValidate(c).ShouldHaveValidationErrorFor(x => x.Impuestos);
    }

    [Fact]
    public void Editar_con_capacidad_no_positiva_es_invalido()
    {
        var c = new EditarHabitacionCommand(Guid.CreateVersion7(), [1], "Suite", 100m, 19m, "Piso 3", Capacidad: 0);
        _editar.TestValidate(c).ShouldHaveValidationErrorFor(x => x.Capacidad);
    }

    [Fact]
    public void CambiarEstado_sin_rowversion_es_invalido()
    {
        var c = new CambiarEstadoHabitacionCommand(Guid.CreateVersion7(), [], EstadoHabitacion.Habilitada);
        _estado.TestValidate(c).ShouldHaveValidationErrorFor(x => x.RowVersion);
    }

    [Fact]
    public void CambiarEstado_con_estado_fuera_de_rango_es_invalido()
    {
        var c = new CambiarEstadoHabitacionCommand(Guid.CreateVersion7(), [1], (EstadoHabitacion)99);
        _estado.TestValidate(c).ShouldHaveValidationErrorFor(x => x.EstadoObjetivo);
    }
}
