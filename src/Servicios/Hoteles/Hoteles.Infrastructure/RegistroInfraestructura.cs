using Hoteles.Domain.Puertos;
using Hoteles.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hoteles.Infrastructure;

/// <summary>Registro de los adaptadores de infraestructura del BC de Hoteles.</summary>
public static class RegistroInfraestructura
{
    public static IServiceCollection AddHotelesInfrastructure(this IServiceCollection servicios, string? cadenaConexion)
    {
        servicios.AddDbContext<HotelesDbContext>(opciones => opciones.UseSqlServer(cadenaConexion));
        servicios.AddScoped<IHotelRepository, HotelRepository>();
        return servicios;
    }
}
