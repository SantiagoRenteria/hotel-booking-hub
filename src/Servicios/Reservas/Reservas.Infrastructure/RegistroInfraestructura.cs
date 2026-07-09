using HotelBookingHub.Comun.Eventos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reservas.Domain.Puertos;
using Reservas.Infrastructure.Disponibilidad;
using Reservas.Infrastructure.Mensajeria;
using Reservas.Infrastructure.Persistencia;

namespace Reservas.Infrastructure;

/// <summary>Registro de los adaptadores de infraestructura del BC de Reservas.</summary>
public static class RegistroInfraestructura
{
    public static IServiceCollection AddReservasInfrastructure(this IServiceCollection servicios, string? cadenaConexion)
    {
        servicios.AddDbContext<ReservasDbContext>(opciones => opciones.UseSqlServer(cadenaConexion));
        servicios.AddScoped<IReservaRepository, ReservaRepository>();

        // Placeholders de 1.6a (reemplazados en E3 y 1.6b respectivamente).
        servicios.AddSingleton<IDisponibilidadHabitacion, DisponibilidadHabitacionSembrada>();
        servicios.AddSingleton<IPublicadorEventos, PublicadorEventosLog>();

        return servicios;
    }
}
