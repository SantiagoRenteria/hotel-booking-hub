using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Domain.Habitaciones;

namespace Hoteles.Application.Habitaciones.CrearHabitacion;

/// <summary>Comando para añadir una habitación a un hotel (AC-E2.4.1). El <see cref="HotelId"/> lo fija la ruta anidada.</summary>
public sealed record CrearHabitacionCommand(
    Guid HotelId,
    string Tipo,
    decimal CostoBase,
    decimal Impuestos,
    string Ubicacion,
    EstadoHabitacion Estado) : ICommand<Result<HabitacionResponseDto>>;
