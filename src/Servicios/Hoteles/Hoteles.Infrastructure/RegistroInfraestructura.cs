using HotelBookingHub.Comun.Eventos;
using Hoteles.Application.Abstracciones;
using Hoteles.Domain.Puertos;
using Hoteles.Infrastructure.Mensajeria;
using Hoteles.Infrastructure.Outbox;
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
        servicios.AddScoped<IHabitacionRepository, HabitacionRepository>();

        // Emisión transaccional de eventos de catálogo (2.5, Opción U): la cola stagea la fila de outbox en el
        // mismo DbContext; el repositorio la confirma junto al dominio en un único SaveChanges (sin TransactionBehavior).
        servicios.AddScoped<IColaOutbox, ColaOutbox>();

        // Relay del outbox (productor): procesador + BackgroundService que sondea y publica.
        servicios.AddSingleton<OpcionesRelayOutbox>();
        servicios.AddSingleton<ProcesadorOutbox>();
        servicios.AddHostedService<RelayOutbox>();

        // Placeholder de transporte (reemplazado por Dapr pub/sub).
        servicios.AddSingleton<IPublicadorEventos, PublicadorEventosLog>();

        return servicios;
    }
}
