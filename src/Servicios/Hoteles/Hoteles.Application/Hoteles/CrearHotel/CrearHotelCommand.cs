using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Domain.Hoteles;

namespace Hoteles.Application.Hoteles.CrearHotel;

/// <summary>Comando para dar de alta un hotel en el catálogo del agente (FR-1).</summary>
public sealed record CrearHotelCommand(
    string Nombre,
    string Ciudad,
    string Direccion,
    string Descripcion,
    EstadoHotel Estado) : ICommand<Result<HotelResponseDto>>;
