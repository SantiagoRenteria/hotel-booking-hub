using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reservas.Application.Abstracciones;
using Reservas.Domain.Puertos;
using Reservas.Infrastructure.Disponibilidad;
using Reservas.Infrastructure.Mensajeria;
using Reservas.Infrastructure.Outbox;
using Reservas.Infrastructure.Persistencia;

namespace Reservas.Infrastructure;

/// <summary>Registro de los adaptadores de infraestructura del BC de Reservas.</summary>
public static class RegistroInfraestructura
{
    public static IServiceCollection AddReservasInfrastructure(this IServiceCollection servicios, string? cadenaConexion)
    {
        servicios.AddDbContext<ReservasDbContext>(opciones => opciones.UseSqlServer(cadenaConexion));

        // Escritura atómica: repositorio de staging + unidad de trabajo transaccional + cola de outbox +
        // TransactionBehavior (eslabón interno del pipeline, solo comandos). MessageId scoped por petición.
        servicios.AddScoped<IReservaRepository, ReservaRepository>();
        servicios.AddScoped<EjecutorTransaccional>();
        servicios.AddScoped<ContextoMensajeria>();
        servicios.AddScoped<IColaOutbox, ColaOutbox>();
        servicios.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        // Relay del outbox (productor): procesador + BackgroundService que sondea y publica.
        servicios.AddSingleton<OpcionesRelayOutbox>();
        servicios.AddSingleton<ProcesadorOutbox>();
        servicios.AddHostedService<RelayOutbox>();

        // Placeholders de 1.6a (reemplazados en E3 y por Dapr pub/sub respectivamente).
        servicios.AddSingleton<IDisponibilidadHabitacion, DisponibilidadHabitacionSembrada>();
        servicios.AddSingleton<IPublicadorEventos, PublicadorEventosLog>();

        return servicios;
    }
}
