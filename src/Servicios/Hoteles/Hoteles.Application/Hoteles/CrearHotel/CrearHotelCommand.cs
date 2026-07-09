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

/// <summary>Recurso devuelto al crear el hotel (201): identidad UUID v7, nombre, ciudad y estado. Sin `Seq`.</summary>
public sealed record HotelResponseDto(Guid Id, string Nombre, string Ciudad, string Estado);
